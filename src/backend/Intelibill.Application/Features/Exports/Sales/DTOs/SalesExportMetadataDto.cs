namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportMetadataDto(
    string ShopName,
    string? ShopAddress,
    string? ShopGstin,
    string GeneratedBy,
    DateTimeOffset GeneratedAt,
    DateOnly StartDate,
    DateOnly EndDate,
    string ExportLevel,
    string AccountingExportVersion = SalesExportMetadataDto.CurrentAccountingExportVersion)
{
    /// <summary>
    /// 2: returns post in their own period from their accounting snapshot, voids as linked reversals,
    /// and every voucher balances. Kept with each file so old and new exports can be told apart.
    /// </summary>
    public const string CurrentAccountingExportVersion = "2";
}
