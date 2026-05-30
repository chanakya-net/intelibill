using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.SearchSellables;

public sealed class SearchSellablesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IServiceRepository serviceRepository)
{
    public async Task<ErrorOr<IReadOnlyList<SellableDto>>> HandleAsync(
        SearchSellablesQuery query,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
            return Errors.Auth.UserNotFound;

        var shop = await shopRepository.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(query.UserId, query.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        var searchTerm = query.SearchTerm.Trim();

        var goodsBatches = query.IsBarcodeLookup
            ? await inventoryBatchRepository.GetAvailableByBarcodeAsync(query.ShopId, searchTerm, cancellationToken)
            : await inventoryBatchRepository.SearchAvailableByProductNameOrBatchNumberAsync(query.ShopId, searchTerm, cancellationToken);
        IReadOnlyList<Service> services = query.IsBarcodeLookup
            ? []
            : await serviceRepository.SearchActiveAsync(query.ShopId, searchTerm, cancellationToken);

        var sellables = goodsBatches
            .Select(batch => SellableDto.FromInventoryBatch(
                batch.Id,
                batch.Item?.Barcode ?? string.Empty,
                batch.Item?.Name ?? string.Empty,
                batch.BatchNumber,
                batch.Quantity,
                batch.SalesPrice,
                batch.Mrp,
                batch.TaxRatePercent,
                batch.TaxIncluded,
                batch.PurchaseTaxIncluded,
                batch.ExpiryDate))
            .Concat(services
                .Select(service => SellableDto.FromService(
                    service.Id,
                    service.Code,
                    service.Name,
                    service.Description,
                    service.Price,
                    service.HsnCode,
                    service.TaxRatePercent,
                    service.TaxIncluded)))
            .ToList();

        return sellables;
    }
}
