using ErrorOr;
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
    IBusinessClock businessClock)
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
        Transition transition,
        CancellationToken cancellationToken)
    {
        // Authority is read live, not from the token, before anything is reserved or replayed.
        if (!await IsOwnerAsync(shopId, actorUserId, cancellationToken))
            return Errors.PostingPeriod.OwnerRequired;

        var shop = await shopRepository.GetByIdAsync(shopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var key = FinancialOperationHasher.NormalizeKey(operationKey)
            ?? throw new InvalidOperationException("The validator requires an operation key.");
        var trimmedReason = reason.Trim();
        var hash = FinancialOperationHasher.Compute(new TransitionPayload(type.ToString(), periodStart, trimmedReason));

        return await operationExecutor.ExecuteAsync(
            new FinancialOperationScope(shopId, type, key),
            actorUserId,
            shopId,
            hash,
            ct => TransitionAsync(shop.BusinessTimeZoneId, shopId, actorUserId, periodStart, trimmedReason, transition, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<PostingPeriodDto>> TransitionAsync(
        string timeZoneId,
        Guid shopId,
        Guid actorUserId,
        DateOnly periodStart,
        string reason,
        Transition transition,
        CancellationToken cancellationToken)
    {
        var (start, end) = PostingPeriod.MonthOf(periodStart);
        await periodRepository.EnsureExistsAsync(shopId, start, end, cancellationToken);
        var period = await periodRepository.LockForTransitionAsync(shopId, start, cancellationToken)
            ?? throw new InvalidOperationException("The posting period row was not visible after it was ensured.");

        // The period lock is held, so no posting commits meanwhile; authority is judged again against
        // the live membership because it may have changed since the first check.
        if (!await IsOwnerAsync(shopId, actorUserId, cancellationToken))
            return Errors.PostingPeriod.OwnerRequired;

        var applied = transition(period, businessClock.UtcNow, businessClock.Today(timeZoneId));
        if (applied.IsError)
            return applied.Errors;

        await periodRepository.AddEventAsync(applied.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var events = await periodRepository.GetEventsAsync(shopId, [period.Id], cancellationToken);
        return PostingPeriodDtoMapper.Map(period, events);
    }

    private async Task<bool> IsOwnerAsync(Guid shopId, Guid userId, CancellationToken cancellationToken) =>
        await shopRepository.GetActiveRoleAsync(userId, shopId, cancellationToken) == ShopRole.Owner;
}
