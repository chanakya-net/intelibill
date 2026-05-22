namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public sealed record OfflineSalesSyncResponseDto(IReadOnlyList<OfflineSaleSyncResultDto> Results);

public sealed record OfflineSaleSyncResultDto(
    string ClientSaleId,
    string Status,
    Guid? SaleId,
    string? InvoiceNumber,
    IReadOnlyList<OfflineSaleSyncErrorDto> Errors)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record OfflineSaleSyncErrorDto(string Code, string Message);
