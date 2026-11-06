using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Services.Payments;

internal sealed class InvoiceBalanceReader(ApplicationDbContext context) : IInvoiceBalanceReader
{
    public async Task<IReadOnlyList<CustomerInvoiceBalanceDto>> GetForCustomerAsync(
        Guid shopId,
        Guid customerId,
        IReadOnlyCollection<Guid>? saleIds,
        CancellationToken cancellationToken)
    {
        var salesQuery = context.Sales
            .AsNoTracking()
            .Where(s => s.ShopId == shopId && s.CustomerId == customerId && s.DueAmount > 0m);
        if (saleIds is not null)
            salesQuery = salesQuery.Where(s => saleIds.Contains(s.Id));

        var sales = await salesQuery
            .OrderBy(s => s.SoldAt)
            .ThenBy(s => s.InvoiceNumber)
            .Select(s => new { s.Id, s.InvoiceNumber, s.SoldAt, s.DueAmount })
            .ToListAsync(cancellationToken);
        if (sales.Count == 0)
            return [];

        var ids = sales.Select(s => s.Id).ToList();
        var returned = await context.SaleReturns
            .AsNoTracking()
            .Where(r => r.ShopId == shopId && ids.Contains(r.SaleId) && !r.IsVoided)
            .GroupBy(r => r.SaleId)
            .Select(g => new { SaleId = g.Key, Amount = g.Sum(r => r.DueReductionAmount) })
            .ToDictionaryAsync(r => r.SaleId, r => r.Amount, cancellationToken);
        var allocated = await ActiveAllocatedAsync(shopId, PaymentAllocationTargetType.CustomerInvoice, ids, cancellationToken);

        return sales
            .Select(s =>
            {
                var returnCredit = returned.GetValueOrDefault(s.Id);
                var paid = allocated.GetValueOrDefault(s.Id);
                return new CustomerInvoiceBalanceDto(
                    s.Id,
                    s.InvoiceNumber,
                    s.SoldAt,
                    s.DueAmount,
                    paid,
                    returnCredit,
                    Math.Max(0m, s.DueAmount - returnCredit - paid),
                    DueDate: null);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SupplierReceiptBalanceDto>> GetForSupplierAsync(
        Guid shopId,
        Guid supplierId,
        IReadOnlyCollection<Guid>? receiptValueIds,
        CancellationToken cancellationToken)
    {
        var query =
            from r in context.PurchaseReceiptValues.AsNoTracking()
            join b in context.InventoryBatches.AsNoTracking() on r.InventoryBatchId equals b.Id
            join i in context.Items.AsNoTracking() on r.ItemId equals i.Id
            where r.ShopId == shopId
                && r.SupplierId == supplierId
                && r.Status == PurchaseReceiptValueStatus.Resolved
                && r.EffectiveTotalCost > 0m
                && !b.IsVoided
            select new { r.Id, r.InventoryBatchId, b.BatchNumber, ItemName = i.Name, r.ReceivedAt, Payable = r.EffectiveTotalCost!.Value };
        if (receiptValueIds is not null)
            query = query.Where(r => receiptValueIds.Contains(r.Id));

        var receipts = await query
            .OrderBy(r => r.ReceivedAt)
            .ThenBy(r => r.BatchNumber)
            .ToListAsync(cancellationToken);
        if (receipts.Count == 0)
            return [];

        var allocated = await ActiveAllocatedAsync(
            shopId,
            PaymentAllocationTargetType.SupplierReceipt,
            receipts.Select(r => r.Id).ToList(),
            cancellationToken);

        return receipts
            .Select(r =>
            {
                var paid = allocated.GetValueOrDefault(r.Id);
                return new SupplierReceiptBalanceDto(
                    r.Id,
                    r.InventoryBatchId,
                    r.BatchNumber,
                    r.ItemName,
                    r.ReceivedAt,
                    r.Payable,
                    paid,
                    Math.Max(0m, r.Payable - paid));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<PaymentReceiptSummaryDto>> GetReceiptsAsync(
        Guid shopId,
        PaymentPartyType partyType,
        Guid partyId,
        CancellationToken cancellationToken)
    {
        var receipts = await context.PaymentReceipts
            .AsNoTracking()
            .Where(r => r.ShopId == shopId && r.PartyType == partyType && r.PartyId == partyId)
            .OrderByDescending(r => r.BusinessDate)
            .ThenByDescending(r => r.ReceivedAt)
            .Select(r => new
            {
                r.Id,
                r.Amount,
                r.Method,
                r.ExternalReference,
                r.BusinessDate,
                r.IsLegacy,
                r.LedgerEntryId,
                IsReversed = context.PaymentReversals.Any(v => v.ReceiptId == r.Id),
                Allocated = context.PaymentAllocations.Where(a => a.ReceiptId == r.Id).Sum(a => (decimal?)a.Amount) ?? 0m,
            })
            .ToListAsync(cancellationToken);

        return receipts
            .Select(r => new PaymentReceiptSummaryDto(
                r.Id,
                r.Amount,
                r.Method,
                r.ExternalReference,
                r.BusinessDate,
                r.IsLegacy,
                r.IsReversed,
                r.IsReversed ? 0m : r.Allocated,
                r.IsReversed ? 0m : r.Amount - r.Allocated,
                r.LedgerEntryId))
            .ToList();
    }

    private async Task<Dictionary<Guid, decimal>> ActiveAllocatedAsync(
        Guid shopId,
        PaymentAllocationTargetType targetType,
        IReadOnlyCollection<Guid> targetIds,
        CancellationToken cancellationToken) =>
        await context.PaymentAllocations
            .AsNoTracking()
            .Where(a => a.ShopId == shopId
                && a.TargetType == targetType
                && targetIds.Contains(a.TargetId)
                && !context.PaymentReversals.Any(v => v.ReceiptId == a.ReceiptId))
            .GroupBy(a => a.TargetId)
            .Select(g => new { TargetId = g.Key, Amount = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(r => r.TargetId, r => r.Amount, cancellationToken);
}
