using Intelibill.Application.Common.Security;
using Intelibill.Application.Common.Interfaces;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.PostingPeriods.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PostingPeriods.Services;

/// <summary>The one flow a close and a reopen share; they differ only in the transition applied.</summary>
internal sealed class PostingPeriodTransitionRunner(
    IShopRepository shopRepository,
    IPostingPeriodRepository periodRepository,
    IFinancialOperationExecutor operationExecutor,
    IUnitOfWork unitOfWork,
    IBusinessClock businessClock,
    ILiveSessionAuthority authority)
{
    public delegate ErrorOr<PostingPeriodEvent> Transition(PostingPeriod period, DateTimeOffset now, DateOnly today);

    private sealed record TransitionPayload(string Action, DateOnly PeriodStart, string Reason);

    public async Task<ErrorOr<PostingPeriodDto>> RunAsync(
        FinancialOperationType type,
        Guid shopId,
        Guid actorUserId,
        DateOnly periodStart,
        string reason,
        string operationKey,
        SessionClaims? session,
        Transition transition,
        CancellationToken cancellationToken)
    {
        // Authority is read live, not from the token, before anything is reserved or replayed.
        if (await AuthorizeAsync(shopId, actorUserId, session, cancellationToken) is { } denial)
            return denial;

        var shop = await shopRepository.GetByIdAsync(shopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var key = FinancialOperationHasher.NormalizeKey(operationKey)
            ?? throw new InvalidOperationException("The validator requires an operation key.");
        var trimmedReason = reason.Trim();
        var hash = FinancialOperationHasher.Compute(new TransitionPayload(type.ToString(), periodStart, trimmedReason));

        return await operationExecutor.ExecuteAsync(
            new FinancialOperationScope(shopId, type, key)
            {
                SuspensionAction = LicenceAction.SourceCorrection,
                RecheckReplayAuthority = ct => AuthorizeAsync(shopId, actorUserId, session, ct),
                RecheckBeforeCommit = ct => AuthorizeAsync(shopId, actorUserId, session, ct),
            },
            actorUserId,
            shopId,
            hash,
            ct => TransitionAsync(shop.BusinessTimeZoneId, shopId, actorUserId, periodStart, trimmedReason, session, transition, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<PostingPeriodDto>> TransitionAsync(
        string timeZoneId,
        Guid shopId,
        Guid actorUserId,
        DateOnly periodStart,
        string reason,
        SessionClaims? session,
        Transition transition,
        CancellationToken cancellationToken)
    {
        var (start, _) = PostingPeriod.MonthOf(periodStart);
        var locked = await periodRepository.LockForTransitionAsync(shopId, start, cancellationToken);
        if (locked.Outcome == RowLockOutcome.TimedOut)
            return Errors.PostingPeriod.Busy;

        var period = locked.Period
            ?? throw new InvalidOperationException("The posting period row was not visible after it was ensured.");

        // The period lock is held, so no posting commits meanwhile; authority is judged again against
        // the live membership because it may have changed since the first check.
        if (await AuthorizeAsync(shopId, actorUserId, session, cancellationToken) is { } denial)
            return denial;

        var applied = transition(period, businessClock.UtcNow, businessClock.Today(timeZoneId));
        if (applied.IsError)
            return applied.Errors;

        await periodRepository.AddEventAsync(applied.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var events = await periodRepository.GetEventsAsync(shopId, [period.Id], cancellationToken);
        return PostingPeriodDtoMapper.Map(period, events);
    }

    private async Task<Error?> AuthorizeAsync(Guid shopId, Guid userId, SessionClaims? session, CancellationToken ct)
    {
        if (await authority.SessionDenialAsync(session, userId, shopId, ct) is { } denied)
            return denied;
        return await shopRepository.GetActiveRoleAsync(userId, shopId, ct) == ShopRole.Owner
            ? null
            : Errors.PostingPeriod.OwnerRequired;
    }
}
