using Intelibill.Application.Features.Items.Barcodes;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class BarcodeLabelRepository(ApplicationDbContext context) : IBarcodeLabelRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<IReadOnlyList<BarcodeLabelPrintRow>> GetRowsAsync(
        Guid activeShopId,
        IReadOnlyList<PrintBarcodeLabelItemRequest> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return [];

        var shopName = await _context.Shops
            .AsNoTracking()
            .Where(shop => shop.Id == activeShopId)
            .Select(shop => shop.Name)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(shopName))
            return [];

        var rows = new List<BarcodeLabelPrintRow>(items.Count);

        var itemIds = items
            .Where(item => item.InventoryBatchId is null)
            .Select(item => item.ItemId)
            .Distinct()
            .ToArray();

        if (itemIds.Length > 0)
        {
            var itemOnlyRows = await _context.Items
                .AsNoTracking()
                .Where(item => item.ShopId == activeShopId && itemIds.Contains(item.Id))
                .Select(item => new BarcodeLabelPrintRow(
                    item.Id,
                    null,
                    item.Name,
                    item.Barcode,
                    shopName,
                    _context.InventoryBatches
                        .Where(batch => batch.ShopId == activeShopId && batch.ItemId == item.Id && !batch.IsVoided)
                        .OrderByDescending(batch => batch.CreatedAt)
                        .ThenByDescending(batch => batch.Id)
                        .Select(batch => (decimal?)batch.Mrp)
                        .FirstOrDefault(),
                    _context.InventoryBatches
                        .Where(batch => batch.ShopId == activeShopId && batch.ItemId == item.Id && !batch.IsVoided)
                        .OrderByDescending(batch => batch.CreatedAt)
                        .ThenByDescending(batch => batch.Id)
                        .Select(batch => (decimal?)batch.SalesPrice)
                        .FirstOrDefault()))
                .ToListAsync(cancellationToken);

            rows.AddRange(itemOnlyRows);
        }

        var requestedBatchIds = items
            .Where(item => item.InventoryBatchId.HasValue)
            .Select(item => item.InventoryBatchId!.Value)
            .Distinct()
            .ToArray();

        if (requestedBatchIds.Length > 0)
        {
            var requestedItemIds = items
                .Where(item => item.InventoryBatchId.HasValue)
                .Select(item => item.ItemId)
                .Distinct()
                .ToArray();

            var batchRows = await _context.InventoryBatches
                .AsNoTracking()
                .Where(batch => batch.ShopId == activeShopId && !batch.IsVoided && requestedBatchIds.Contains(batch.Id))
                .Join(
                    _context.Items.AsNoTracking().Where(item => item.ShopId == activeShopId && requestedItemIds.Contains(item.Id)),
                    batch => batch.ItemId,
                    item => item.Id,
                    (batch, item) => new BarcodeLabelPrintRow(
                        item.Id,
                        batch.Id,
                        item.Name,
                        item.Barcode,
                        shopName,
                        batch.Mrp,
                        batch.SalesPrice))
                .ToListAsync(cancellationToken);

            rows.AddRange(batchRows);
        }

        return rows;
    }
}
