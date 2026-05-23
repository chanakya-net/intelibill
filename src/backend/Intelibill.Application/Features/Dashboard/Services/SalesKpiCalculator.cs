using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Dashboard.Services;

internal static class SalesKpiCalculator
{
    internal const decimal CreditShareWarningThreshold = 0.40m;

    internal sealed record SalesKpis(
        int SalesCount,
        decimal SalesBooked,
        decimal NetSalesBooked,
        decimal CashCollected,
        decimal TotalCost,
        decimal WastageCost,
        decimal TotalTax,
        decimal ProfitBeforeTax,
        decimal ProfitAfterTax);

    internal static SalesKpis CalculateSalesKpis(
        IReadOnlyCollection<Sale> sales,
        IReadOnlyCollection<SaleReturn>? saleReturns = null,
        IReadOnlyCollection<InventoryAdjustment>? adjustmentLosses = null)
    {
        var salesBooked = sales.Sum(s => s.TotalAmount);
        var cashCollected = sales.Sum(s => s.PaidAmount);
        var totalCost = sales.SelectMany(s => s.Items).Sum(i => i.CostPrice * i.Quantity);
        var totalTax = sales.Sum(s => s.TotalTaxAmount);
        var activeReturns = saleReturns?.Where(r => !r.IsVoided).ToList() ?? [];
        var refundAmount = activeReturns.Sum(r => r.TotalRefundAmount);
        var refundTax = activeReturns
            .SelectMany(r => r.Items)
            .Sum(item =>
            {
                if (item.ApprovedRefundAmount <= 0m || item.MaxRefundAmount <= 0m || item.TaxAmount <= 0m)
                {
                    return 0m;
                }

                var tax = item.ApprovedRefundAmount * item.TaxAmount / item.MaxRefundAmount;
                return Math.Round(tax, 2, MidpointRounding.AwayFromZero);
            });
        var restockableCost = activeReturns
            .SelectMany(r => r.Items)
            .Where(i => i.Condition == SaleReturnCondition.Restockable)
            .Sum(i => i.OriginalCostPrice * i.Quantity);
        var wastageCost = activeReturns
            .SelectMany(r => r.Items)
            .Where(i => i.Condition == SaleReturnCondition.Wastage)
            .Sum(i => i.OriginalCostPrice * i.Quantity);
        var adjustmentLossCost = adjustmentLosses?.Where(a => a.Direction == InventoryAdjustmentDirection.Decrease && !a.IsVoided).Sum(a => a.CostImpact) ?? 0m;
        var netSalesBooked = salesBooked - refundAmount;
        var netCost = totalCost - restockableCost;
        var netTax = totalTax - refundTax;

        return new SalesKpis(
            sales.Count,
            salesBooked,
            netSalesBooked,
            cashCollected,
            totalCost,
            wastageCost + adjustmentLossCost,
            totalTax,
            netSalesBooked - netCost - adjustmentLossCost,
            netSalesBooked - netTax - netCost - adjustmentLossCost);
    }

    internal static PaymentMixDto CalculatePaymentMix(IReadOnlyCollection<Sale> sales)
    {
        var cash = 0m;
        var upi = 0m;
        var card = 0m;
        var credit = 0m;

        foreach (var sale in sales)
        {
            var due = Math.Max(0m, sale.DueAmount);
            var paidPortion = Math.Max(0m, sale.TotalAmount - due);

            if (sale.PaymentMethod == PaymentMethod.Credit)
            {
                credit += sale.TotalAmount;
                continue;
            }

            credit += due;

            switch (sale.PaymentMethod)
            {
                case PaymentMethod.Cash:
                    cash += paidPortion;
                    break;
                case PaymentMethod.UPI:
                    upi += paidPortion;
                    break;
                case PaymentMethod.Card:
                    card += paidPortion;
                    break;
                default:
                    cash += paidPortion;
                    break;
            }
        }

        return new PaymentMixDto(Cash: cash, Upi: upi, Card: card, Credit: credit);
    }
}
