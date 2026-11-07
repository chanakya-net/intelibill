using Intelibill.Application.Features.Payments.Services;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Services.Cancellations;

public sealed record SaleCancellationEvaluation(IReadOnlyList<SaleCancellationReasonDto> Reasons, IReadOnlyList<SaleCancellationPreviewLineDto> Lines, decimal CreditRestorationAmount)
{
    public bool Eligible => Reasons.Count == 0;
}

/// <summary>Read-only evaluation; all posting facts must be reread under the sale/customer locks.</summary>
public sealed class SaleCancellationPolicy(ISaleReturnRepository returns, ICreditNoteRedemptionRepository redemptions,
    IPostingPeriodRepository periods, IInventoryBatchRepository batches, IInventoryRepository inventory,
    IPaymentReceiptRepository payments, IInvoiceBalanceReader balances, ICustomerLedgerEntryRepository ledger)
{
    public async Task<SaleCancellationEvaluation> EvaluateAsync(Guid shopId, Sale sale, BusinessDay day, CancellationToken ct)
    {
        List<SaleCancellationReasonDto> reasons = [];
        void Block(string code, string message) => reasons.Add(new($"SaleCancellation.{code}", message));
        if (sale.Status == SaleStatus.Cancelled) Block("AlreadyCancelled", "This sale has already been cancelled.");
        if (sale.Source != SaleSource.Online) Block("OfflineOriginBlocked", "Offline-origin invoices cannot be cancelled here.");
        var d = sale.InvoiceDocument;
        var validShape = HasValidShape(d);
        var matched = validShape ? MatchLines(sale, d) : null;
        if (d is null || d.Version != 2) Block("LegacySnapshotBlocked", "A valid version-2 issued receipt is required.");
        else if (!validShape) Block("SnapshotMismatch", "The stored receipt is missing frozen seller, buyer, line or amount facts. Review the original receipt.");
        else
        {
            if (d.DocumentType != InvoiceDocumentType.Receipt || d.Seller.TaxRegime != TaxRegime.Unregistered)
                Block("TaxInvoiceBlocked", "Only a receipt issued by an unregistered seller can be cancelled here.");
            if (!Consistent(sale, d) || matched is null) Block("SnapshotMismatch", "The frozen receipt and sale facts do not reconcile or line attribution is ambiguous. Review the original receipt.");
        }
        if (sale.BusinessDate.Year != day.Date.Year || sale.BusinessDate.Month != day.Date.Month)
            Block("OtherMonthBlocked", "Use the return or financial adjustment workflow for a sale from another month.");
        var month = PostingPeriod.MonthOf(day.Date);
        if ((await periods.GetRangeAsync(shopId, month.Start, month.End, ct)).Any(x => x.State == PostingPeriodState.Closed))
            Block("ClosedMonthBlocked", "The posting month is closed.");
        if ((await returns.GetBySaleAsync(shopId, sale.Id, ct)).Count > 0) Block("ReturnHistoryBlocked", "A sale with any return history, including voided returns, cannot be cancelled.");
        var redemptionRows = await redemptions.FindAsync(x => x.ShopId == shopId && x.SaleId == sale.Id, ct);
        var credit = Math.Max(sale.CreditNoteAppliedAmount, redemptionRows.Sum(x => x.Amount));
        if (credit > 0) Block("CreditRestorationRequired", $"Applied credit of {credit:0.00} requires review. No credit balance is restored by cancellation.");
        if (await payments.HasActiveAllocationsAsync(shopId, PaymentAllocationTargetType.CustomerInvoice, sale.Id, ct))
            Block("CollectionsRequireReview", "Active collections allocated to this invoice require review.");
        if (sale.DueAmount > 0)
        {
            var safe = false;
            if (sale.CustomerId is { } customerId)
            {
                var invoice = (await balances.GetForCustomerAsync(shopId, customerId, [sale.Id], ct)).SingleOrDefault();
                var receipts = await balances.GetReceiptsAsync(shopId, PaymentPartyType.Customer, customerId, ct);
                safe = invoice?.OutstandingAmount == sale.DueAmount && await ledger.GetCustomerBalanceAsync(shopId, customerId, ct) >= sale.DueAmount && !receipts.Any(x => !x.IsReversed && x.UnallocatedAmount > 0);
            }
            if (!safe) Block("CollectionsRequireReview", "Original due cannot be fully reduced: invoice outstanding, account balance or unallocated collections require review.");
        }
        if (sale.PaidAmount > 0 && sale.PaymentMethod != PaymentMethod.Cash)
            Block("ExternalRefundWorkflowRequired", "External refund is not recorded by this cancellation workflow.");
        if (sale.TotalAmount < 0 || sale.PaidAmount < 0 || sale.DueAmount < 0 || sale.PaidAmount + sale.DueAmount != sale.TotalAmount)
            Block("SettlementMismatch", "Original paid and due amounts must exactly settle the receipt total.");
        List<SaleCancellationPreviewLineDto> lines = [];
        for (var i = 0; i < sale.Items.Count; i++)
        {
            var line = sale.Items[i]; var frozen = matched?.GetValueOrDefault(line.Id);
            lines.Add(new(line.Id, line.ItemId, line.InventoryBatchId, line.Quantity, frozen?.Description ?? line.LineName, frozen?.Uom ?? string.Empty));
            if (line.LineType != SaleLineType.Goods) { Block("ServiceBlocked", "Every line must be recoverable inventory goods."); continue; }
            var batch = line.InventoryBatchId is { } batchId ? await batches.GetByIdAsync(batchId, ct) : null;
            var aggregate = line.ItemId is { } itemId ? await inventory.GetByItemAsync(shopId, itemId, ct) : null;
            if (line.Quantity <= 0 || batch is null || batch.ShopId != shopId || batch.ItemId != line.ItemId || batch.IsVoided || aggregate is null || aggregate.ShopId != shopId)
                Block("StockLinksInvalid", "Original goods must have an existing nonvoided batch and inventory aggregate in this shop.");
        }
        if (lines.Count == 0) Block("StockLinksInvalid", "No original inventory goods were found.");
        return new(reasons, lines, credit);
    }
    private static bool HasValidShape(IssuedInvoiceSnapshot? d) =>
        d is { Seller: not null, Buyer: not null, Totals.Components: not null, Lines: not null } &&
        d.Totals.Components.All(c => c is not null) &&
        d.Lines.All(l => l is { TaxComponents: not null } && l.TaxComponents.All(c => c is not null));

    private static bool Consistent(Sale s, IssuedInvoiceSnapshot d)
    {
        // PostgreSQL timestamptz stores microseconds; the frozen JSON retains 100ns ticks.
        // The online writer gives both facts the same captured instant before persistence.
        if (d.Number != s.InvoiceNumber || d.BusinessDate != s.BusinessDate || d.IssuedAt.UtcTicks / 10 != s.SoldAt.UtcTicks / 10 || d.Lines.Count != s.Items.Count || d.Totals.RoundingAdjustment != 0 || d.Totals.InvoiceTotal != s.TotalAmount ||
            s.TotalTaxAmount != 0 || d.Totals.TotalTax != 0 || d.Totals.Components.Any(x => x.Amount != 0 || x.RatePercent != 0) ||
            d.Totals.TaxableValue + d.Totals.ExemptValue != s.TotalAmount || s.Items.Sum(x => x.TotalAmount) != s.TotalAmount || d.Lines.Sum(x => x.LineTotal) != s.TotalAmount) return false;
        return true;
    }
    private static Dictionary<Guid, InvoiceDocumentLine>? MatchLines(Sale sale, IssuedInvoiceSnapshot? document)
    {
        if (document is null || document.Lines.Count != sale.Items.Count ||
            document.Lines.Select(x => x.LineNumber).Distinct().Count() != document.Lines.Count ||
            document.Lines.Any(x => x.LineNumber < 1 || x.LineNumber > document.Lines.Count)) return null;
        var remaining = document.Lines.ToList();
        Dictionary<Guid, InvoiceDocumentLine> result = [];
        foreach (var line in sale.Items)
        {
            var matches = remaining.Where(f => f.Description == line.LineName && f.HsnOrSac == line.HsnCode &&
                f.Quantity == line.Quantity && f.UnitPrice == line.SalesPrice && f.DiscountAmount == line.ItemDiscountAmount + line.SaleDiscountAmount &&
                f.TaxableValue == line.TaxableAmount && f.TaxRatePercent == line.TaxRatePercent && f.LineTotal == line.TotalAmount &&
                line.TaxableAmount == line.TotalAmount && line.TaxAmount == 0 && line.TaxRatePercent == 0 &&
                f.TaxComponents.All(c => c.Amount == 0 && c.RatePercent == 0) && !string.IsNullOrWhiteSpace(f.Uom)).ToList();
            if (matches.Count == 0 || matches.Select(f => (f.Description, f.Uom, f.IsExempt)).Distinct().Count() != 1) return null;
            var frozen = matches[0]; result.Add(line.Id, frozen); remaining.Remove(frozen);
        }
        return result;
    }
}
