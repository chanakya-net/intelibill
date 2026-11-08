using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Common.Posting;
using Intelibill.Domain.Interfaces;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Queries.GetAvailableBatches;

public sealed class GetAvailableBatchesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    SaleExpiryGuard expiryGuard, IBusinessClock clock, IExpiryPolicyFence expiryFence, IUnitOfWork unitOfWork)
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

        return await PostingTransaction.RunAsync<IReadOnlyList<AvailableBatchDto>>(unitOfWork, async ct =>
        {
            var locked = await expiryFence.AcquireAsync(query.ShopId, false, ct);
            if (locked.IsError) return locked.Errors;
            var date = clock.Today(shop.BusinessTimeZoneId);
            var batches = query.IsBarcodeLookup
                ? await inventoryBatchRepository.GetAvailableByBarcodeAsync(
                    query.ShopId, query.SearchTerm, cancellationToken)
                : await inventoryBatchRepository.SearchAvailableByProductNameOrBatchNumberAsync(
                    query.ShopId, query.SearchTerm, cancellationToken);

            var projected = new List<AvailableBatchDto>();
            foreach (var b in batches.OrderBy(x => x.Item?.Name).ThenBy(x => x.ItemId).ThenBy(x => x.ExpiryDate is null).ThenBy(x => x.ExpiryDate).ThenBy(x => x.BatchNumber, StringComparer.Ordinal).ThenBy(x => x.Id))
            {
                var decision = await expiryGuard.EvaluateBatchAsync(b, date, ct);
                if (!query.IncludeUnavailable && !decision.Allowed) continue;
                projected.Add(new AvailableBatchDto(
                    b.Item?.Barcode ?? "",
                    b.Item?.Name ?? "",
                    b.BatchNumber,
                    b.Id,
                    b.Quantity,
                    b.SalesPrice,
                    b.Mrp,
                    b.TaxRatePercent,
                    b.TaxIncluded,
                    b.PurchaseTaxIncluded,
                    b.ExpiryDate,
                    b.Item?.Uom ?? "",
                    b.Item?.QuantityScale ?? 0,
                    b.QuarantinedQuantity, decision.AvailableQuantity, b.Version, decision));
            }
            return projected;
        }, cancellationToken);
    }
}
