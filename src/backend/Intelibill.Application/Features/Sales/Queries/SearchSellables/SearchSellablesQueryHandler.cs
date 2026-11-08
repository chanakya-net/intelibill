using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Common.Posting;
using Intelibill.Domain.Interfaces;
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
    IServiceRepository serviceRepository, SaleExpiryGuard expiryGuard, IBusinessClock clock, IExpiryPolicyFence expiryFence, IUnitOfWork unitOfWork)
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

        return await PostingTransaction.RunAsync<IReadOnlyList<SellableDto>>(unitOfWork, async ct =>
        {
            var locked = await expiryFence.AcquireAsync(query.ShopId, false, ct);
            if (locked.IsError) return locked.Errors;
            var date = clock.Today(shop.BusinessTimeZoneId);
            var searchTerm = query.SearchTerm.Trim();

            var goodsBatches = query.IsBarcodeLookup
                ? await inventoryBatchRepository.GetAvailableByBarcodeAsync(query.ShopId, searchTerm, cancellationToken)
                : await inventoryBatchRepository.SearchAvailableByProductNameOrBatchNumberAsync(query.ShopId, searchTerm, cancellationToken);
            IReadOnlyList<Service> services = query.IsBarcodeLookup
                ? []
                : await serviceRepository.SearchActiveAsync(query.ShopId, searchTerm, cancellationToken);

            var evaluation = await expiryGuard.BeginEvaluationAsync(query.ShopId, date, goodsBatches.Select(x => x.ItemId), ct);
            var sellables = new List<SellableDto>();
            foreach (var batch in goodsBatches.OrderBy(x => x.Item?.Name).ThenBy(x => x.ItemId).ThenBy(x => x.ExpiryDate is null).ThenBy(x => x.ExpiryDate).ThenBy(x => x.BatchNumber, StringComparer.Ordinal).ThenBy(x => x.Id))
            {
                var decision = evaluation.EvaluateBatch(batch);
                if (!query.IncludeUnavailable && !decision.Allowed) continue;
                sellables.Add(SellableDto.FromInventoryBatch(batch.Id, batch.Item?.Barcode ?? string.Empty, batch.Item?.Name ?? string.Empty,
                    batch.BatchNumber, batch.Quantity, batch.SalesPrice, batch.Mrp, batch.TaxRatePercent, batch.TaxIncluded,
                    batch.PurchaseTaxIncluded, batch.ExpiryDate, batch.Item?.Uom, batch.Item?.QuantityScale) with
                { QuarantinedQuantity = batch.QuarantinedQuantity, AvailableQuantity = decision.AvailableQuantity, Version = batch.Version, Sellability = decision });
            }
            sellables.AddRange(services
                    .Select(service => SellableDto.FromService(
                        service.Id,
                        service.Code,
                        service.Name,
                        service.Description,
                        service.Price,
                        service.HsnCode,
                        service.TaxRatePercent,
                        service.TaxIncluded)));

            return sellables;
        }, cancellationToken);
    }
}
