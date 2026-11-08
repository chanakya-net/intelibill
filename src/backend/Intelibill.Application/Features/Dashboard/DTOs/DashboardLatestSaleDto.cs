using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record DashboardLatestSaleDto(
    Guid SaleId,
    string InvoiceNumber,
    string CustomerDisplayName,
    DateTimeOffset SoldAt,
    decimal TotalAmount)
{
    public SaleStatus Status { get; init; }
    public string? CancellationReference { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
}
