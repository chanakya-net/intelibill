namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record DashboardLatestSaleDto(
    Guid SaleId,
    string InvoiceNumber,
    string CustomerDisplayName,
    DateTimeOffset SoldAt,
    decimal TotalAmount);
