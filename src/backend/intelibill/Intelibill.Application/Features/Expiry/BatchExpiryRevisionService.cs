using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
namespace Intelibill.Application.Features.Expiry;

/// <summary>The caller owns an exclusive expiry fence, version check, transaction and save.</summary>
public sealed class BatchExpiryRevisionService(IExpiryPolicyRepository policies, IBatchExpiryRevisionRepository revisions, IShopRepository shops, IBusinessClock clock)
{
    public async Task<ErrorOr<Success>> ValidateReceiptAsync(Guid shopId, Guid itemId, DateOnly? expiry, DateOnly? manufacturing, CancellationToken ct)
    {
        if (expiry.HasValue && manufacturing > expiry) return Error.Validation("Expiry.InvalidDateOrder", "Manufacturing date must not follow expiry date.");
        var policy = await policies.GetEffectiveAsync(shopId, itemId, null, ct);
        if (!policy.HistoryKnown || policy.Policy is null) return Errors.Expiry.HistoryUnknown;
        if (policy.Policy.ExpiryRequired && expiry is null) return Error.Validation("Expiry.RequiredDateMissing", "This item requires an expiry date.");
        return Result.Success;
    }
    public async Task<ErrorOr<Success>> ReviseAsync(InventoryBatch batch, DateOnly? expiryDate, DateOnly? manufacturingDate, Guid actor, string reason, CancellationToken ct)
    {
        if (batch.ExpiryDate == expiryDate && batch.ManufacturingDate == manufacturingDate) return Result.Success;
        if (await shops.GetActiveRoleAsync(actor, batch.ShopId, ct) is not (ShopRole.Owner or ShopRole.Manager)) return Errors.Expiry.ManagerRequired;
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) return Errors.Expiry.ReasonRequired;
        if (batch.Version == 0) return Errors.Inventory.BatchVersionConflict;
        var check = await ValidateReceiptAsync(batch.ShopId, batch.ItemId, expiryDate, manufacturingDate, ct);
        if (check.IsError) return check.Errors;
        var state = await policies.GetStateAsync(batch.ShopId, ct);
        if (state is null) return Errors.Expiry.HistoryUnknown;
        var version = state.Advance();
        await revisions.AddAsync(BatchExpiryRevision.Create(batch, expiryDate, manufacturingDate, actor, reason, clock.UtcNow, version), ct);
        return batch.ReviseRetailTerms(batch.BatchNumber, batch.Mrp, batch.SalesPrice, batch.TaxRatePercent, batch.TaxIncluded, expiryDate, manufacturingDate, actor);
    }
}
