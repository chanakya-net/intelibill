using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface ISaleRepository : IRepository<Sale>
{
    Task<Sale?> GetByIdAsync(Guid saleId, Guid shopId, CancellationToken cancellationToken = default);
    Task<Sale?> GetByIdempotencyKeyAsync(Guid shopId, Guid actorUserId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<Sale?> GetByClientSaleIdAsync(Guid shopId, string deviceId, string clientSaleId, CancellationToken cancellationToken = default);
    Task<Sale?> GetByInvoiceNumberAsync(Guid shopId, string invoiceNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetByCustomerAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetByShopAndDateAsync(Guid shopId, DateOnly reportingDay, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sale>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SaleHistoryReadModel> Items, int TotalCount)> GetHistoryAsync(
        SaleHistoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<SalesHistorySummaryReadModel> GetHistorySummaryAsync(
        Guid shopId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);
}

public sealed record SaleHistoryFilter(
    Guid ShopId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Search,
    string? Status,
    int PageNumber,
    int PageSize);

public sealed record SaleHistoryReadModel(
    Guid SaleId,
    string InvoiceNumber,
    Guid? CustomerId,
    PaymentMethod PaymentMethod,
    DateTimeOffset SoldAt,
    decimal PaidAmount,
    decimal DueAmount,
    decimal TotalBeforeDiscount,
    decimal TotalDiscountAmount,
    decimal TotalAmount,
    decimal TotalTaxAmount,
    string? CustomerName,
    string? CustomerPhone,
    int ItemCount,
    IReadOnlyList<string> ReturnNumbers,
    string Status,
    decimal RefundAmount,
    decimal DueReductionAmount);

public sealed record SalesHistorySummaryReadModel(
    decimal PeriodSales,
    int InvoiceCount,
    decimal RefundAmount);
