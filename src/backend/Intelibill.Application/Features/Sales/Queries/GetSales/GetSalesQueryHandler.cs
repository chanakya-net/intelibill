using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Queries.GetSales;

public sealed class GetSalesQueryHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISaleRepository saleRepository)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int DefaultLookbackDays = 30;

    public async Task<ErrorOr<SalesHistoryResultDto>> Handle(
        GetSalesQuery query,
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = query.From
            ?? (query.To.HasValue ? query.To.Value.AddDays(-(DefaultLookbackDays - 1)) : today.AddDays(-(DefaultLookbackDays - 1)));
        var endDate = query.To ?? today;

        var pageNumber = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : query.PageSize;
        if (pageSize > MaxPageSize)
        {
            pageSize = MaxPageSize;
        }

        var filter = new SaleHistoryFilter(
            ShopId: query.ShopId,
            StartDate: startDate,
            EndDate: endDate,
            Search: query.Search,
            Status: query.Status,
            PageNumber: pageNumber,
            PageSize: pageSize);

        var (rows, totalCount) = await saleRepository.GetHistoryAsync(filter, cancellationToken);
        var summary = await saleRepository.GetHistorySummaryAsync(query.ShopId, startDate, endDate, cancellationToken);

        var items = rows.Select(s => new SaleListItemDto(
            s.SaleId,
            s.InvoiceNumber,
            s.CustomerId,
            s.PaymentMethod,
            s.SoldAt,
            s.PaidAmount,
            s.DueAmount,
            s.TotalBeforeDiscount,
            s.TotalDiscountAmount,
            s.TotalAmount,
            s.TotalTaxAmount,
            s.CustomerName,
            s.CustomerPhone,
            s.ItemCount,
            s.ReturnNumbers,
            s.Status,
            s.RefundAmount,
            s.DueReductionAmount)).ToList();

        return new SalesHistoryResultDto(
            Items: items,
            TotalCount: totalCount,
            PageNumber: pageNumber,
            PageSize: pageSize,
            Summary: new SalesHistorySummaryDto(
                summary.PeriodSales,
                summary.InvoiceCount,
                summary.RefundAmount));
    }
}
