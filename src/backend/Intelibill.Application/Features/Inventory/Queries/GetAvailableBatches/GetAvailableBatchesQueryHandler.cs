using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Queries.GetAvailableBatches;

public sealed class GetAvailableBatchesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInventoryBatchRepository inventoryBatchRepository)
{
    public async Task<ErrorOr<IReadOnlyList<AvailableBatchDto>>> Handle(
        GetAvailableBatchesQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var batches = await inventoryBatchRepository.GetAvailableByBarcodeAsync(
            query.ShopId, query.SearchTerm, cancellationToken);

        return batches
            .Select(b => new AvailableBatchDto(
                b.Item?.Barcode ?? query.SearchTerm,
                b.Item?.Name ?? query.SearchTerm,
                b.BatchNumber,
                b.Quantity,
                b.SalesPrice,
                b.Mrp,
                b.TaxRatePercent,
                b.TaxIncluded,
                b.ExpiryDate))
            .ToList();
    }
}
