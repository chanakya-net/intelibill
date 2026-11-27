using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Tills.Services;

/// <summary>Validates actual source settlement inside the source-owned transaction, after all period fences.</summary>
public sealed class TillSourceSettlementService(ITillSessionPolicy policy, ITillPostingService posting,
    IShopRepository shops, IBusinessClock clock)
{
    public Task<bool> IsEnabledAsync(Guid shopId, CancellationToken ct = default) => policy.IsEnabledAsync(shopId, ct);

    public async Task<DateOnly> TodayAsync(Guid shopId, CancellationToken ct) =>
        clock.Today((await shops.GetByIdAsync(shopId, ct) ?? throw new InvalidOperationException("Shop unavailable.")).BusinessTimeZoneId);

    public async Task<ErrorOr<TillSession?>> AcquireAsync(Guid shopId, Guid actorId, TillPostingContext? context,
        string? operationKey, TillSettlementEvidence? evidence, decimal actualDelta, DateOnly sourceDate, CancellationToken ct)
    {
        var gate = await posting.GateAsync(shopId, ct);
        if (gate.IsError) return gate.Errors;
        var enabled = gate.Value;
        if (!enabled)
            return context is null && evidence is null ? (TillSession?)null : Errors.Till.Disabled;
        if (FinancialOperationHasher.NormalizeKey(operationKey) is null) return Errors.Operation.InvalidKey;
        if (evidence is null) return Errors.Till.SettlementEvidenceRequired;
        var valid = evidence.Validate();
        if (valid.IsError) return valid.Errors;
        if (evidence.Disposition == TillSettlementDisposition.NoPhysicalMovement) return (TillSession?)null;
        if (actualDelta == 0 || (actualDelta > 0 ? TillSettlementDisposition.MoneyReceived : TillSettlementDisposition.MoneyPaid) != evidence.Disposition)
            return Error.Validation("Till.SettlementDirectionInvalid", "Settlement must match the source's exact signed change.");
        if (evidence.Tender!.AppliedAmount != Math.Abs(actualDelta))
            return Error.Validation("Till.TenderAmountMismatch", "Tender must equal the exact source settlement amount.");
        return await posting.AcquireAsync(shopId, actorId, context, sourceDate, ct);
    }

    public async Task<ErrorOr<Success>> AppendAsync(TillSession? session, TillMovementSource type, Guid sourceId,
        Guid actorId, string? key, TillSettlementEvidence? evidence, CancellationToken ct)
    {
        if (session is null) return Result.Success;
        return await posting.AppendAsync(session, new(type, sourceId, "settlement", [evidence!.Tender!],
            evidence.Disposition == TillSettlementDisposition.MoneyReceived ? 1m : -1m,
            clock.UtcNow, key!.Trim(), evidence.Reason), actorId, ct);
    }
}
