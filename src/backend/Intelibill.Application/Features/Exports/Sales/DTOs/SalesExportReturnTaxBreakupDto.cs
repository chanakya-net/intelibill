namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportReturnTaxBreakupDto(
    decimal TaxRatePercent,
    decimal TaxableAmount,
    decimal TaxAmount);
