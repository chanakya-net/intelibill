using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Exports.Accounting;

/// <summary>
/// Turns returns into their dated accounting postings for a range of business dates: the return on
/// its business date, and its void, if any, as a reversal on the void's business date. A return
/// processed in June and voided in July posts its credit in June and its reversal in July; each
/// export of either month shows only its own posting. The dates were stored when each posting was
/// made, so a later change of the shop's time zone moves nothing.
/// </summary>
public static class ReturnAdjustmentProjector
{
    public static IReadOnlyList<ReturnAdjustmentEntry> Project(
        IEnumerable<SaleReturn> returns,
        IReadOnlyDictionary<Guid, Sale> salesById,
        IReadOnlyDictionary<Guid, string> creditNoteCodesById,
        DateOnly from,
        DateOnly to)
    {
        var entries = new List<ReturnAdjustmentEntry>();
        foreach (var saleReturn in returns)
        {
            if (!salesById.TryGetValue(saleReturn.SaleId, out var sale))
                continue;

            if (InRange(saleReturn.BusinessDate, from, to))
                entries.Add(Entry(saleReturn, sale, creditNoteCodesById, ReturnAdjustmentEntryKind.Return));

            if (saleReturn.IsVoided && saleReturn.VoidedAt is not null && saleReturn.VoidedBusinessDate is { } voidedOn && InRange(voidedOn, from, to))
                entries.Add(Entry(saleReturn, sale, creditNoteCodesById, ReturnAdjustmentEntryKind.VoidReversal));
        }

        return entries
            .OrderBy(e => e.PostedAt)
            .ThenBy(e => e.ReturnNumber, StringComparer.Ordinal)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.SaleReturnId)
            .ToList();
    }

    private static ReturnAdjustmentEntry Entry(
        SaleReturn saleReturn,
        Sale sale,
        IReadOnlyDictionary<Guid, string> creditNoteCodesById,
        ReturnAdjustmentEntryKind kind)
    {
        if (saleReturn.AccountingStatus is null)
            throw new InvalidOperationException($"Sale return {saleReturn.Id} was saved without its accounting snapshot.");

        var sign = kind == ReturnAdjustmentEntryKind.VoidReversal ? -1m : 1m;
        var lineTypes = sale.Items.ToDictionary(i => i.Id, i => i.LineType);

        var lines = saleReturn.Items
            .Select(item => new ReturnAdjustmentLine(
                item.SaleItemId,
                lineTypes.TryGetValue(item.SaleItemId, out var lineType) ? lineType : null,
                item.Condition,
                sign * item.Quantity,
                item.OriginalCostPrice,
                sign * item.MaxRefundAmount,
                item.OriginalTaxRatePercent,
                sign * item.AdjustmentTaxableAmount!.Value,
                sign * item.AdjustmentTaxAmount!.Value,
                sign * item.AdjustmentNonTaxAmount!.Value))
            .ToList();

        var taxComponents = lines
            .GroupBy(l => l.TaxRatePercent)
            .OrderBy(g => g.Key)
            .Select(g => new ReturnAdjustmentTaxComponent(
                g.Key,
                g.Sum(l => l.TaxableAdjustment),
                g.Sum(l => l.TaxAdjustment)))
            .Where(c => c.TaxableAmount != 0m || c.TaxAmount != 0m)
            .ToList();

        var documentTaxComponents = sale.InvoiceDocument is { } document
            ? DocumentComponents(document, taxComponents)
            : null;

        var settlements = saleReturn.Settlements
            .OrderBy(s => s.Sequence)
            .Select(s => new ReturnAdjustmentSettlement(
                s.Kind,
                sign * s.Amount,
                s.CustomerId,
                s.CreditNoteId,
                s.CreditNoteId is { } creditNoteId ? creditNoteCodesById.GetValueOrDefault(creditNoteId) : null))
            .ToList();

        return new ReturnAdjustmentEntry(
            kind,
            kind == ReturnAdjustmentEntryKind.VoidReversal ? saleReturn.VoidedAt!.Value : saleReturn.ProcessedAt,
            kind == ReturnAdjustmentEntryKind.VoidReversal ? saleReturn.VoidedBusinessDate!.Value : saleReturn.BusinessDate,
            saleReturn.Id,
            saleReturn.ReturnNumber,
            saleReturn.ProcessedAt,
            saleReturn.BusinessDate,
            sale.Id,
            sale.InvoiceNumber,
            sale.SoldAt,
            sale.BusinessDate,
            sale.CustomerName,
            saleReturn.AccountingPolicyVersion!,
            sign * saleReturn.OriginalReturnedGross,
            sign * saleReturn.TotalRefundAmount,
            sign * saleReturn.AdjustmentTaxableAmount!.Value,
            sign * saleReturn.AdjustmentTaxAmount!.Value,
            sign * saleReturn.AdjustmentNonTaxAmount!.Value,
            taxComponents,
            settlements,
            lines,
            kind == ReturnAdjustmentEntryKind.VoidReversal ? saleReturn.VoidReason : null,
            documentTaxComponents);
    }

    /// <summary>The returned tax per rate, split with the document's own place of supply and rule.</summary>
    private static List<InvoiceTaxComponent> DocumentComponents(
        IssuedInvoiceSnapshot document,
        IReadOnlyList<ReturnAdjustmentTaxComponent> taxComponents)
    {
        var unionTerritoryTax = GstStateCodes.Find(document.PlaceOfSupplyStateCode)?.IsUnionTerritoryWithoutLegislature == true;
        return taxComponents
            .Where(c => c.TaxRatePercent > 0m)
            .SelectMany(c => InvoiceDocumentBuilder.SplitTax(c.TaxableAmount, c.TaxRatePercent, c.TaxAmount, document.IsInterstate, unionTerritoryTax))
            .Where(c => c.Amount != 0m)
            .ToList();
    }

    private static bool InRange(DateOnly date, DateOnly from, DateOnly to) => date >= from && date <= to;
}
