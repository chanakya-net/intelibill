namespace Intelibill.Application.Features.Exports.Sales.DTOs;

public sealed record SalesExportTaxBreakupDto(
    decimal TaxRatePercent,
    decimal SaleTaxableAmount,
    decimal SaleTaxAmount,
    decimal ReturnTaxableAmount,
    decimal ReturnTaxAmount)
{
    public decimal CancellationTaxableAmount { get; init; }
    public decimal NetTaxableAmount => SaleTaxableAmount - ReturnTaxableAmount - CancellationTaxableAmount;
    public decimal NetTaxAmount => SaleTaxAmount - ReturnTaxAmount;
}
