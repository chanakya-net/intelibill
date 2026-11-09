using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Customers.Queries.GetCustomers;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomerDirectory;

public sealed class GetCustomerDirectoryQueryHandler(ICustomerRepository customers, ICustomerLedgerEntryRepository ledger,
    ISaleRepository sales, IShopRepository shops, IBusinessClock clock)
{
    public async Task<ErrorOr<CustomerDirectoryDto>> HandleAsync(GetCustomerDirectoryQuery query, CancellationToken cancellationToken)
    {
        var shop = await shops.GetByIdAsync(query.ShopId, cancellationToken);
        if (shop is null) return Errors.Shop.ShopNotFound;
        var page = await customers.GetDirectoryAsync(query.ShopId, query.Filter, cancellationToken);
        var enriched = await new GetCustomersQueryHandler(customers, ledger, sales, shops, clock).EnrichAsync(query.ShopId, page.Items, cancellationToken);
        if (enriched.IsError) return enriched.Errors;
        var today = clock.Today(shop.BusinessTimeZoneId);
        var start = new DateOnly(today.Year, today.Month, 1);
        var month = clock.GetUtcWindow(start, start.AddMonths(1).AddDays(-1), shop.BusinessTimeZoneId);
        var summary = await customers.GetDirectorySummaryAsync(query.ShopId, month, cancellationToken);
        return new CustomerDirectoryDto(enriched.Value, page.TotalCount, page.PageNumber, page.PageSize, summary);
    }
}
