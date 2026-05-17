using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Queries.GetProductDetails;

public sealed class GetProductDetailsByNameOrBarcodeQueryHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ISupplierRepository supplierRepository,
    IExternalProductLookupService externalProductLookupService,
    IUnitOfWork unitOfWork)
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

        var createdFromExternalLookup = false;

        if (item is null && !string.IsNullOrWhiteSpace(query.Barcode))
        {
            var normalizedBarcode = query.Barcode.Trim();
            item = await itemRepository.GetByBarcodeAsync(query.ActiveShopId, normalizedBarcode, cancellationToken);

            if (item is null)
            {
                var externalLookupResult = await externalProductLookupService.LookupByBarcodeAsync(
                    normalizedBarcode,
                    query.AuthorizationHeader,
                    cancellationToken);

                if (externalLookupResult.IsError)
                    return externalLookupResult.Errors;

                if (externalLookupResult.Value is not null)
                {
                    var normalizedName = externalLookupResult.Value.ProductName.Trim();
                    if (!string.IsNullOrWhiteSpace(normalizedName))
                    {
                        var normalizedUom = string.IsNullOrWhiteSpace(externalLookupResult.Value.Uom)
                            ? "Unit"
                            : externalLookupResult.Value.Uom.Trim();

                        item = Item.Create(
                            query.ActiveShopId,
                            normalizedName,
                            externalLookupResult.Value.Description,
                            normalizedUom,
                            normalizedBarcode,
                            true,
                            query.UserId);

                        await itemRepository.AddAsync(item, cancellationToken);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        createdFromExternalLookup = true;
                    }
                }
            }
        }

        if (item is null)
            return Error.NotFound("product.not_found", "Product not found");

        // Get the latest batch for this item (ordered by earliest expiry date, then batch number)
        var batches = await inventoryBatchRepository.GetByItemAsync(query.ActiveShopId, item.Id, cancellationToken);

        if (batches.Count == 0)
        {
            if (!createdFromExternalLookup)
                return Error.NotFound("product.no_batches", "Product has no pricing information");

            return new ProductDetailsDto(
                item.Name,
                item.Description ?? string.Empty,
                item.Uom,
                0m,
                0m,
                0m,
                null,
                null,
                null,
                null,
                null);
        }

        var latestBatch = batches[0];

        Guid? supplierId = null;
        string? supplierName = null;
        bool? taxIncluded = null;
        decimal? taxRatePercent = null;

        if (latestBatch.SupplierId.HasValue)
        {
            var supplier = await supplierRepository.GetByIdAsync(latestBatch.SupplierId.Value, cancellationToken);
            if (supplier is not null && supplier.IsActive)
            {
                supplierId = supplier.Id;
                supplierName = supplier.Name;
                taxIncluded = latestBatch.TaxIncluded;
                taxRatePercent = latestBatch.TaxRatePercent;
            }
        }

        return new ProductDetailsDto(
            item.Name,
            item.Description ?? string.Empty,
            item.Uom,
            latestBatch.CostPrice,
            latestBatch.Mrp,
            latestBatch.SalesPrice,
            supplierId,
            supplierName,
            taxIncluded,
            taxRatePercent,
            item.HsnCode);
    }
}
