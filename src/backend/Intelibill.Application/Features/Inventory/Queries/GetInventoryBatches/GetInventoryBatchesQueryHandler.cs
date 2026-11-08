using Intelibill.Application.Common.Posting;
using Intelibill.Domain.Interfaces;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Common.Time;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Queries.GetInventoryBatches;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Queries.GetInventoryBatches;

public sealed class GetInventoryBatchesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IPurchaseReceiptValueRepository purchaseReceiptValueRepository, SaleExpiryGuard expiryGuard, IBusinessClock clock, IExpiryPolicyFence expiryFence, IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<IReadOnlyList<InventoryBatchDto>>> Handle(GetInventoryBatchesQuery query, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        // Verify user has access to shop
        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        return await PostingTransaction.RunAsync<IReadOnlyList<InventoryBatchDto>>(unitOfWork, async ct =>
        {
            var locked = await expiryFence.AcquireAsync(query.ShopId, false, ct);
            if (locked.IsError) return locked.Errors;
            var date = clock.Today(shop.BusinessTimeZoneId);
            var batches = await inventoryBatchRepository.GetByShopAsync(query.ShopId, cancellationToken);
            var receipts = (await purchaseReceiptValueRepository.GetByShopAsync(query.ShopId, cancellationToken))
                .ToDictionary(r => r.InventoryBatchId);

            var result = new List<InventoryBatchDto>();
            var evaluation = await expiryGuard.BeginEvaluationAsync(query.ShopId, date, batches.Select(x => x.ItemId), ct);
            foreach (var batch in batches)
            {
                var decision = evaluation.EvaluateBatch(batch);
                result.Add(ToDto(batch, receipts.GetValueOrDefault(batch.Id)) with { QuarantinedQuantity = batch.QuarantinedQuantity, AvailableQuantity = decision.AvailableQuantity, Sellability = decision });
            }
            return result;
        }, cancellationToken);
    }

    private static InventoryBatchDto ToDto(InventoryBatch b, PurchaseReceiptValue? receipt) =>
        new(
            b.Id,
            b.ShopId,
            b.ItemId,
            b.Item.Name,
            b.Item.Barcode,
            b.BatchNumber,
            b.Quantity,
            b.OriginalQuantity,
            b.CostPrice,
            b.Mrp,
            b.SalesPrice,
            b.TaxRatePercent,
            b.TaxIncluded,
            b.PurchaseTaxIncluded,
            b.ExpiryDate,
            b.ManufacturingDate,
            b.SupplierId,
            null, // SupplierName can be fetched if needed
            b.IsVoided,
            b.CreatedAt,
            b.UpdatedAt,
            b.Version,
            receipt?.Id,
            receipt?.Status,
            receipt?.OriginalTotalCost,
            receipt?.EffectiveTotalCost,
            receipt?.Version,
            b.Item.Uom,
            b.Item.QuantityScale);
}
