using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Queries.GetInventoryBatches;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Queries.GetInventoryBatches;

public sealed class GetInventoryBatchesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInventoryBatchRepository inventoryBatchRepository)
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

        var batches = await inventoryBatchRepository.GetByShopAsync(query.ShopId, cancellationToken);

        return batches.Select(b => new InventoryBatchDto(
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
            b.ExpiryDate,
            b.ManufacturingDate,
            b.SupplierId,
            null, // SupplierName can be fetched if needed
            b.IsVoided,
            b.CreatedAt,
            b.UpdatedAt)).ToList();
    }
}
