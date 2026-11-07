using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;
internal sealed class SupplierReturnRepository(ApplicationDbContext context) : RepositoryBase<SupplierReturn>(context), ISupplierReturnRepository
{
    private IQueryable<SupplierReturn> Details(Guid shopId) => Context.SupplierReturns.AsNoTracking().Where(x=>x.ShopId==shopId).Include(x=>x.Lines).Include(x=>x.Credit).ThenInclude(x=>x!.Lines);
    public Task<SupplierReturn?> GetDetailAsync(Guid shopId, Guid id, CancellationToken cancellationToken) => Details(shopId).AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id,cancellationToken);
    public async Task<IReadOnlyList<SupplierReturn>> GetByReceiptsAsync(Guid shopId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => await Details(shopId).Where(x=>x.Lines.Any(l=>ids.Contains(l.ReceiptValueId))).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<SupplierReturn>> ListAsync(Guid shopId, Guid? supplierId, Guid? receiptId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var q=Details(shopId).AsNoTracking();
        if(supplierId.HasValue) q=q.Where(x=>x.SupplierId==supplierId);
        if(receiptId.HasValue) q=q.Where(x=>x.Lines.Any(l=>l.ReceiptValueId==receiptId));
        if(Enum.TryParse<Intelibill.Domain.Enums.SupplierReturnStatus>(status,out var parsed)) q=q.Where(x=>x.Status==parsed);
        return await q.OrderByDescending(x=>x.ShippedAt).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(cancellationToken);
    }
    public async Task AddCreditAsync(SupplierReturnCredit credit,CancellationToken cancellationToken) => await Context.SupplierReturnCredits.AddAsync(credit,cancellationToken);
    public async Task<IReadOnlyList<PurchaseReceiptValue>> GetReceiptsAsync(Guid shopId,IReadOnlyCollection<Guid> ids,CancellationToken cancellationToken)
    {
        // Query after root locks; reload any entity the command may have resolved before locking.
        var rows=await Context.PurchaseReceiptValues.Where(x=>x.ShopId==shopId && ids.Contains(x.Id)).ToListAsync(cancellationToken);
        foreach(var row in rows) await Context.Entry(row).ReloadAsync(cancellationToken);
        return rows;
    }
    public async Task<IReadOnlyList<PurchaseReceiptValue>> GetGroupReceiptsAsync(Guid shopId,Guid groupId,CancellationToken cancellationToken)
    {
        var lineIds=await Context.PurchaseOrderReceiptLines.AsNoTracking()
            .Where(line=>line.PurchaseOrderReceiptId==groupId && Context.PurchaseOrderReceipts.Any(receipt=>receipt.Id==groupId && receipt.ShopId==shopId))
            .Select(line=>line.Id).ToListAsync(cancellationToken);
        var rows=await Context.PurchaseReceiptValues.AsNoTracking()
            .Where(receipt=>receipt.ShopId==shopId && receipt.PurchaseOrderReceiptLineId.HasValue && lineIds.Contains(receipt.PurchaseOrderReceiptLineId.Value))
            .ToListAsync(cancellationToken);
        // A legacy group with a missing/ambiguous source value is never a smaller valid group.
        if(!rows.Select(row=>row.PurchaseOrderReceiptLineId!.Value).Order().SequenceEqual(lineIds.Order())) return [];
        return rows;
    }
    public async Task<IReadOnlyList<PurchaseReceiptValue>> GetByPoLineIdsAsync(Guid shopId,IReadOnlyCollection<Guid> ids,CancellationToken cancellationToken) => await Context.PurchaseReceiptValues.AsNoTracking().Where(x=>x.ShopId==shopId && x.PurchaseOrderReceiptLineId.HasValue && ids.Contains(x.PurchaseOrderReceiptLineId.Value)).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<SupplierReturn>> GetByLedgerEntriesAsync(Guid shopId,IReadOnlyCollection<Guid> ids,CancellationToken cancellationToken) => await Details(shopId).Where(x=>x.Credit!=null && x.Credit.Lines.Any(l=>l.SupplierLedgerEntryId.HasValue && ids.Contains(l.SupplierLedgerEntryId.Value))).ToListAsync(cancellationToken);
    public Task<bool> HasCountHistoryAsync(Guid shopId,Guid batchId,CancellationToken cancellationToken) => Context.StockCounts.AnyAsync(x=>x.ShopId==shopId && x.InventoryBatchId==batchId,cancellationToken);
}
internal sealed class PurchaseReceiptReversalRepository(ApplicationDbContext context) : RepositoryBase<PurchaseReceiptReversal>(context), IPurchaseReceiptReversalRepository
{
    public async Task<IReadOnlyList<PurchaseReceiptReversal>> GetByReceiptsAsync(Guid shopId,IReadOnlyCollection<Guid> ids,CancellationToken cancellationToken) => await Context.PurchaseReceiptReversals.AsNoTracking().Where(x=>x.ShopId==shopId && ids.Contains(x.ReceiptValueId)).ToListAsync(cancellationToken);
}
