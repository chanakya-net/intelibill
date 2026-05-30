namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportTaxBreakupDto(
    decimal TaxRatePercent,
    decimal SaleTaxableAmount,
    decimal SaleTaxAmount,
    decimal ReturnTaxableAmount,
    decimal ReturnTaxAmount)
{
    public decimal NetTaxableAmount => SaleTaxableAmount - ReturnTaxableAmount;
    public decimal NetTaxAmount => SaleTaxAmount - ReturnTaxAmount;
}
