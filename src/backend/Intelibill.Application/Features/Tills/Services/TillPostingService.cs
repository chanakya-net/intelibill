using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using TillErrors = Intelibill.Application.Common.Errors.Errors.Till;

namespace Intelibill.Application.Features.Tills.Services;

/// <summary>Uses the caller's transaction and previously acquired period fences; stages only.</summary>
public sealed class TillPostingService(ITillRepository tills, IShopRepository shops, IBankAccountRepository banks,
    ITillSessionPolicy policy, IBusinessClock clock) : ITillPostingService
{
    private readonly Dictionary<Guid, (TillSession Session, Guid Actor, string? Override)> _acquired = [];

    public async Task<ErrorOr<TillSession?>> AcquireAsync(Guid shopId, Guid actorId, TillPostingContext? context,
        DateOnly sourceBusinessDate, CancellationToken ct = default)
    {
        if (!policy.IsEnabled(shopId))
            return context is null ? (TillSession?)null : TillErrors.Disabled;
        if (context is null || context.SessionId == Guid.Empty) return TillErrors.SessionRequired;
        var session = await tills.GetSessionAsync(shopId, context.SessionId, ct);
        if (session is null) return TillErrors.SessionNotFound;
        var authorized = await policy.AuthorizeAsync(shopId, actorId, session.CashierId, context.OverrideReason, ct);
        if (authorized.IsError) return authorized.Errors;
        var outcome = await tills.LockSessionAsync(shopId, session.Id, ct);
        if (outcome != RowLockOutcome.Acquired) return outcome == RowLockOutcome.TimedOut ? TillErrors.Busy : TillErrors.SessionNotFound;
        session = await tills.ReloadSessionAsync(shopId, session.Id, ct);
        if (session is null) return TillErrors.SessionNotFound;
        authorized = await policy.AuthorizeAsync(shopId, actorId, session.CashierId, context.OverrideReason, ct);
        if (authorized.IsError) return authorized.Errors;
        var shop = await shops.GetByIdAsync(shopId, ct);
        if (shop is null) return TillErrors.NotFound;
        var date = policy.ValidateTradingDate(session, sourceBusinessDate, clock.Today(shop.BusinessTimeZoneId));
        if (date.IsError) return date.Errors;
        _acquired[session.Id] = (session, actorId, context.OverrideReason?.Trim());
        return session;
    }

    public async Task<ErrorOr<Success>> AppendAsync(TillSession session, TillSourcePosting source, Guid actorId, CancellationToken ct = default)
    {
        if (!_acquired.TryGetValue(session.Id, out var acquisition) || !ReferenceEquals(acquisition.Session, session) || acquisition.Actor != actorId)
            return TillErrors.Forbidden;
        if (session.State != TillSessionState.Open) return TillErrors.Closed;
        if (source.Lines is null || source.Lines.Count > 1)
            return TillErrors.MixedTenderUnsupported;
        if (source.Lines.Any(line => line is null))
            return TillErrors.TenderRequired;
        if (source.Direction is not (1m or -1m) || !Enum.IsDefined(source.SourceType) || source.SourceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(source.Purpose) || source.Purpose.Length > 100 || string.IsNullOrWhiteSpace(source.OperationKey) ||
            source.OperationKey.Length > 128 || source.Reason?.Length > 500 || source.OccurredAt < session.OpenedAt || source.OccurredAt > clock.UtcNow ||
            !TillMoney.IsValid(source.StoreCreditIssued) || !TillMoney.IsValid(source.StoreCreditRedeemed) || !TillMoney.IsValid(source.ReceivableChange))
            return Error.Validation("Till.SourceInvalid", "A valid source, operation and signed settlement amounts are required.");
        if (source.SourceType == TillMovementSource.ManualCash && (source.Lines.Count != 1 || source.Lines[0].Method != ReceiptTenderMethod.Cash || string.IsNullOrWhiteSpace(source.Reason)))
            return Error.Validation("Till.ReasonRequired", "Manual cash transfers require cash and a reason.");
        foreach (var line in source.Lines)
        {
            var valid = line.Validate();
            if (valid.IsError) return valid.Errors;
            if (line.BankAccountId is { } bankId && (await banks.GetByIdAsync(bankId, ct))?.ShopId != session.ShopId)
                return Error.Validation("Till.TenderReferenceInvalid", "The bank account must belong to this shop.");
        }
        if (await tills.FindSourceAsync(session.ShopId, source.SourceType, source.SourceId, source.Purpose.Trim(), ct) is not null)
            return TillErrors.DuplicateSource;
        // Everything that can reject the posting is checked before the domain methods advance counters.
        var recorded = TillSourceRecord.Record(session, source.SourceType, source.SourceId, source.Purpose, source.OperationKey,
            actorId, source.OccurredAt, clock.UtcNow, source.Reason, acquisition.Override,
            new(source.StoreCreditIssued, source.StoreCreditRedeemed, source.ReceivableChange));
        if (recorded.IsError) return recorded.Errors;
        TillMovement? movement = null;
        if (source.Lines.Count == 1)
        {
            var result = TillMovement.Record(session, recorded.Value, 1, source.Lines[0], source.Direction, actorId, clock.UtcNow, null);
            if (result.IsError) throw new InvalidOperationException("A prevalidated till movement was rejected.");
            movement = result.Value;
        }
        await tills.AddSourceAsync(recorded.Value, ct);
        if (movement is not null) await tills.AddMovementAsync(movement, ct);
        return Result.Success;
    }
}
