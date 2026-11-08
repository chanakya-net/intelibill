using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Expiry;

public sealed class ExpiryPolicyService(IExpiryPolicyRepository policies, IShopRepository shops, IExpiryPolicyFence fence,
    IUnitOfWork unitOfWork, IBusinessClock clock)
{
    public async Task<ErrorOr<ExpiryPolicyStateDto>> ReadAsync(Guid shopId, Guid actor, Guid? itemId, CancellationToken ct)
    {
        if (await shops.GetActiveRoleAsync(actor, shopId, ct) is null) return Errors.Expiry.MembershipRequired;
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var locked = await fence.AcquireAsync(shopId, false, ct);
            if (locked.IsError) { await unitOfWork.RollbackTransactionAsync(ct); return locked.Errors; }
            var result = await ReadLockedAsync(shopId, itemId, ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return result;
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    private async Task<ErrorOr<ExpiryPolicyStateDto>> ReadLockedAsync(Guid shopId, Guid? itemId, CancellationToken ct)
    {
        if (itemId.HasValue && !await policies.ItemExistsAsync(shopId, itemId.Value, ct)) return Errors.Expiry.ItemNotFound;
        var state = await policies.GetStateAsync(shopId, ct);
        var defaults = await policies.GetLatestRevisionAsync(shopId, null, null, ct);
        var initial = await policies.GetInitialRevisionAsync(shopId, ct);
        if (state is null || defaults?.Policy is null || !defaults.Policy.IsValid() || defaults.CatalogVersion != state.DefaultPolicyVersion || initial?.Policy != Intelibill.Domain.ValueObjects.ExpiryPolicy.Default || initial.ItemId is not null || initial.PreFeatureBaseline != state.PreFeatureBaseline)
            return Errors.Expiry.HistoryUnknown;
        var item = itemId.HasValue ? await policies.GetLatestRevisionAsync(shopId, itemId.Value, null, ct) : null;
        var effective = item?.Policy ?? defaults.Policy;
        if (!effective.IsValid() || effective.Version > state.CatalogVersion) return Errors.Expiry.HistoryUnknown;
        return new ExpiryPolicyStateDto(defaults.Policy, item?.Policy, effective, state.CatalogVersion);
    }

    public async Task<ErrorOr<ExpiryPolicyStateDto>> UpdateAsync(Guid shopId, Guid actor, Guid? itemId, ExpiryPolicyUpdateRequest request, CancellationToken ct)
    {
        if (await shops.GetActiveRoleAsync(actor, shopId, ct) != ShopRole.Owner) return Errors.Expiry.OwnerRequired;
        if (request.ExpectedCatalogVersion <= 0) return Errors.Expiry.PolicyVersionConflict;
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) return Errors.Expiry.ReasonRequired;
        if ((!itemId.HasValue && request.Policy is null) || request.Policy is not null && !request.Policy.ToPolicy(1).IsValid())
            return Errors.Expiry.PolicyInvalid;
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var locked = await fence.AcquireAsync(shopId, true, ct);
            if (locked.IsError) { await unitOfWork.RollbackTransactionAsync(ct); return locked.Errors; }
            // Repeat live authority after waiting for the fence; request claims never authorize a policy change.
            if (await shops.GetActiveRoleAsync(actor, shopId, ct) != ShopRole.Owner)
            { await unitOfWork.RollbackTransactionAsync(ct); return Errors.Expiry.OwnerRequired; }
            var before = await ReadLockedAsync(shopId, itemId, ct);
            if (before.IsError) { await unitOfWork.RollbackTransactionAsync(ct); return before.Errors; }
            var state = (await policies.GetStateAsync(shopId, ct))!;
            if (state.CatalogVersion != request.ExpectedCatalogVersion)
            { await unitOfWork.RollbackTransactionAsync(ct); return Errors.Expiry.PolicyVersionConflict; }
            var version = state.Advance(!itemId.HasValue);
            await policies.AddRevisionAsync(ExpiryPolicyRevision.Create(shopId, itemId, version, request.Policy?.ToPolicy(version), actor, request.Reason, clock.UtcNow), ct);
            await unitOfWork.SaveChangesAsync(ct);
            var result = await ReadLockedAsync(shopId, itemId, ct);
            if (result.IsError) { await unitOfWork.RollbackTransactionAsync(ct); return result.Errors; }
            await unitOfWork.CommitTransactionAsync(ct);
            return result;
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<ErrorOr<ExpiryReadinessDto>> ReadinessAsync(Guid shopId, Guid actor, int page, int pageSize, CancellationToken ct)
    {
        if (await shops.GetActiveRoleAsync(actor, shopId, ct) is not (ShopRole.Owner or ShopRole.Manager)) return Errors.Expiry.ManagerRequired;
        if (page <= 0 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue) return Errors.Expiry.PageInvalid;
        var shop = await shops.GetByIdAsync(shopId, ct);
        if (shop is null) return Errors.Shop.ShopNotFound;
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var locked = await fence.AcquireAsync(shopId, false, ct);
            if (locked.IsError) { await unitOfWork.RollbackTransactionAsync(ct); return locked.Errors; }
            var policy = await ReadLockedAsync(shopId, null, ct);
            if (policy.IsError) { await unitOfWork.RollbackTransactionAsync(ct); return policy.Errors; }
            var date = clock.Today(shop.BusinessTimeZoneId);
            var result = await policies.GetReadinessAsync(shopId, date, page, pageSize, ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return new ExpiryReadinessDto(date, policy.Value.CatalogVersion, page, pageSize, result);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }
}
