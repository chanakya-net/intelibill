using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using System.Data.Common;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Items.Queries.GetProductDetails;

public sealed class GetProductDetailsByNameOrBarcodeQueryHandler(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    ISupplierRepository supplierRepository,
    IExternalProductLookupService externalProductLookupService,
    DefinitionCreationTransaction creation,
    ISuspensionAccessReader suspensionAccess,
    IUnitOfWork unitOfWork)
{
    [LicenceAction(LicenceAction.History)]
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
            var lookup = await FindOrImportAsync(query, query.Barcode.Trim(), cancellationToken);
            if (lookup.IsError)
                return lookup.Errors;
            item = lookup.Value;
        }

        return item is null
            ? Error.NotFound("product.not_found", "Product not found")
            : await BuildDetailsAsync(query.ActiveShopId, item, cancellationToken);
    }

    private async Task<ErrorOr<Item?>> FindOrImportAsync(
        GetProductDetailsByNameOrBarcodeQuery query, string barcode, CancellationToken ct)
    {
        var existing = await itemRepository.GetByBarcodeAsync(query.ActiveShopId, barcode, ct);
        if (existing is not null)
            return existing;

        // The catalogue can be slow. Hold neither the database transaction nor the licence fence here.
        var external = await externalProductLookupService.LookupByBarcodeAsync(barcode, query.AuthorizationHeader, ct);
        if (external.IsError)
            return external.Errors;
        if (external.Value is not { } product || string.IsNullOrWhiteSpace(product.ProductName))
            return (Item?)null;

        return await ImportAsync(query, barcode, product, ct);
    }

    private async Task<ErrorOr<Item?>> ImportAsync(GetProductDetailsByNameOrBarcodeQuery query,
        string barcode, ExternalProductLookupResult product, CancellationToken ct)
    {
        try
        {
            return await creation.RunAsync<Item?>(query.Session, (lease, token) =>
                ReadOrStageImportAsync(query, barcode, product, lease, token), ct);
        }
        catch (Exception exception) when (unitOfWork.IsWriteFailure(exception) && exception.InnerException is DbException { SqlState: "23505" })
        {
            // The shop/barcode unique index resolves races with any other creator, including intake.
            // The losing transaction has rolled back. Read the winner under current authority.
            var winner = await creation.RunAsync<Item?>(query.Session, (lease, token) =>
                ReadOrStageImportAsync(query, barcode, null, lease, token), ct);
            if (winner.IsError || winner.Value is not null)
                return winner;
            throw;
        }
    }

    private async Task<ErrorOr<Item?>> ReadOrStageImportAsync(GetProductDetailsByNameOrBarcodeQuery query,
        string barcode, ExternalProductLookupResult? product, ShopLicenceLease lease, CancellationToken ct)
    {
        var existing = await itemRepository.GetByBarcodeAsync(query.ActiveShopId, barcode, ct);
        if (existing is not null)
        {
            var denial = await suspensionAccess.ReadActiveShopDenialAsync(query.ActiveShopId, LicenceAction.History, ct);
            return denial is { } error ? error : existing;
        }
        if (lease.Decision.EnforcedDenial() is { } blocked)
            return blocked;
        if (product is null)
            return (Item?)null;

        var uom = string.IsNullOrWhiteSpace(product.Uom) ? "Unit" : product.Uom.Trim();
        var item = Item.Create(query.ActiveShopId, product.ProductName.Trim(), product.Description,
            uom, barcode, true, query.UserId);
        await itemRepository.AddAsync(item, ct);
        return item;
    }

    private async Task<ProductDetailsDto> BuildDetailsAsync(Guid shopId, Item item, CancellationToken cancellationToken)
    {
        // Get the latest batch for this item (ordered by earliest expiry date, then batch number)
        var batches = await inventoryBatchRepository.GetByItemAsync(shopId, item.Id, cancellationToken);

        if (batches.Count == 0)
        {
            return new ProductDetailsDto(
                item.Name,
                item.Description ?? string.Empty,
                item.Uom,
                0m,
                0m,
                0m,
                null,
                null,
                item.DefaultTaxIncluded,
                item.DefaultTaxRatePercent,
                item.HsnCode);
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

        taxIncluded ??= item.DefaultTaxIncluded;
        taxRatePercent ??= item.DefaultTaxRatePercent;

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
            item.HsnCode,
            latestBatch.PurchaseTaxIncluded);
    }
}
