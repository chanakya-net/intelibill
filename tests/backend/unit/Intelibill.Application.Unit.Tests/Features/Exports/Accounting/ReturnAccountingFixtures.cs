using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Unit.Tests.Features.Exports.Accounting;

/// <summary>A ₹118 (₹100 + 18%) one-unit sale and returns of it, posted the way the return handler posts them.</summary>
internal static class ReturnAccountingFixtures
{
    public static Sale Sale(Guid shopId, string invoiceNumber = "INV-001", Guid? customerId = null, DateTimeOffset? soldAt = null, decimal quantity = 1m)
    {
        var saleItem = SaleItem.CreateGoods(
            shopId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            lineName: "Rice",
            lineCode: "RICE-1",
            quantity: quantity,
            costPrice: 80m,
            salesPrice: 100m,
            mrp: 120m,
            taxRatePercent: 18m,
            isPriceIncludingTax: false,
            hasPriceMismatch: false);

        return Domain.Entities.Sale.Create(
            shopId,
            invoiceNumber,
            customerId,
            customerName: customerId.HasValue ? "Asha" : null,
            customerPhone: null,
            PaymentMethod.Cash,
            soldAt ?? new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero),
            paidAmount: saleItem.TotalAmount,
            dueAmount: 0m,
            totalAmount: saleItem.TotalAmount,
            totalTaxAmount: saleItem.TaxAmount,
            [saleItem]);
    }

    /// <summary>A return of one unit, posted with the given split and settlement legs, or left unposted.</summary>
    public static SaleReturn Return(
        Sale sale,
        decimal approved,
        decimal taxable,
        decimal tax,
        decimal nonTax = 0m,
        decimal dueReduction = 0m,
        ReturnPayoutDestination? destination = ReturnPayoutDestination.Refund,
        Guid? creditNoteId = null,
        DateTimeOffset? processedAt = null,
        string returnNumber = "RET-001",
        SaleReturnCondition condition = SaleReturnCondition.Restockable,
        bool post = true)
    {
        var saleItem = sale.Items[0];
        var payout = approved - dueReduction;
        var saleReturn = SaleReturn.Record(
            sale.ShopId,
            sale.Id,
            returnNumber,
            processedAt ?? new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero),
            global::Intelibill.Domain.ValueObjects.BusinessDay.InDefaultZone(processedAt ?? new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero)),
            Guid.NewGuid(),
            notes: null,
            totalRefundAmount: approved,
            dueReductionAmount: dueReduction,
            payoutAmount: payout,
            payoutDestination: payout > 0m ? destination : null,
            totalTaxableAmount: 100m,
            totalTaxAmount: 18m,
            customerBalanceBefore: null,
            customerBalanceAfter: null,
            [new SaleReturnLineInput(
                sale.ShopId,
                saleItem.Id,
                1m,
                condition,
                80m,
                100m,
                18m,
                false,
                MaxRefundAmount: 118m,
                ApprovedRefundAmount: approved,
                TaxableAmount: 100m,
                TaxAmount: 18m,
                Notes: approved == 118m ? null : "Adjusted refund")]).Value;

        if (!post)
            return saleReturn;

        var settlements = new List<ReturnSettlementInput>();
        if (dueReduction > 0m)
            settlements.Add(new ReturnSettlementInput(ReturnSettlementKind.ReceivableReduction, dueReduction, sale.CustomerId));
        if (payout > 0m)
        {
            settlements.Add(destination == ReturnPayoutDestination.CreditNote
                ? new ReturnSettlementInput(ReturnSettlementKind.StoreCreditIssued, payout, sale.CustomerId, creditNoteId ?? Guid.NewGuid())
                : new ReturnSettlementInput(ReturnSettlementKind.ExternalRefund, payout, sale.CustomerId));
        }

        var snapshot = ReturnAccountingSnapshot.Create(
            "proportional-line-tax/1",
            [new ReturnLineAccounting(saleItem.Id, taxable, tax, nonTax)],
            settlements).Value;
        var posted = saleReturn.PostAccounting(snapshot);
        if (posted.IsError)
            throw new InvalidOperationException(posted.FirstError.Description);

        return saleReturn;
    }
}
