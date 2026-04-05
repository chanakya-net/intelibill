using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Queries.GetProductDetails;

public sealed class GetProductDetailsByNameOrBarcodeQueryHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository)
{
    public async Task<ErrorOr<ProductDetailsDto>> HandleAsync(
        GetProductDetailsByNameOrBarcodeQuery query,
        CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var callerMembership = caller.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (callerMembership is null)
            return Errors.Shop.MembershipNotFound;

        // Try to find by product name first, then by barcode
        var item = !string.IsNullOrWhiteSpace(query.ProductName)
            ? await itemRepository.GetByNameAsync(query.ActiveShopId, query.ProductName, cancellationToken)
            : null;

        if (item is null && !string.IsNullOrWhiteSpace(query.Barcode))
        {
            item = await itemRepository.GetByBarcodeAsync(query.ActiveShopId, query.Barcode, cancellationToken);
        }

        if (item is null)
            return Error.NotFound("product.not_found", "Product not found");

        // Get the latest batch for this item (ordered by earliest expiry date, then batch number)
        var batches = await inventoryBatchRepository.GetByItemAsync(query.ActiveShopId, item.Id, cancellationToken);

        if (batches.Count == 0)
            return Error.NotFound("product.no_batches", "Product has no pricing information");

        var latestBatch = batches[0];

        return new ProductDetailsDto(
            item.Description ?? string.Empty,
            item.Uom,
            latestBatch.CostPrice,
            latestBatch.Mrp,
            latestBatch.SalesPrice);
    }
}
