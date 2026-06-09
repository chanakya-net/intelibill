using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class InventoryBatchRepository(ApplicationDbContext context)
    : RepositoryBase<InventoryBatch>(context), IInventoryBatchRepository
{
    public async Task<IReadOnlyList<ExpiringBatchAlertReadModel>> GetExpiringBatchAlertsAsync(
        Guid shopId,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var expiryCutoff = today.AddDays(7);

        return await DbSet
            .AsNoTracking()
            .Include(b => b.Item)
            .Where(b =>
                b.ShopId == shopId
                && !b.IsVoided
                && b.Quantity > 0
                && b.ExpiryDate.HasValue
                && b.ExpiryDate.Value >= today
                && b.ExpiryDate.Value <= expiryCutoff)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.Item.Name)
            .ThenBy(b => b.BatchNumber)
            .ThenBy(b => b.Id)
            .Take(5)
            .Select(b => new ExpiringBatchAlertReadModel(
                b.Id,
                b.Item.Name,
                b.BatchNumber,
                b.ExpiryDate!.Value,
                b.Quantity))
            .ToListAsync(cancellationToken);
    }

    public IAsyncEnumerable<InventoryBatch> StreamActiveSellableWithItemByShopAsync(
        Guid shopId,
        CancellationToken cancellationToken = default) =>
        DbSet
            .AsNoTracking()
            .Include(b => b.Item)
            .Where(b => b.ShopId == shopId
                && !b.IsVoided
                && b.Quantity > 0
                && b.Item.IsActive)
            .OrderBy(b => b.Item.Name)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .AsAsyncEnumerable();

    public async Task<IReadOnlyList<InventoryBatch>> GetByItemAsync(Guid shopId, Guid itemId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(b => b.ShopId == shopId && b.ItemId == itemId)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .ToListAsync(cancellationToken);

    public async Task<InventoryBatch?> GetByBatchNumberAsync(Guid shopId, Guid itemId, string batchNumber, CancellationToken cancellationToken = default)
    {
        var normalizedBatchNumber = batchNumber.Trim();
        return await DbSet.FirstOrDefaultAsync(
            b => b.ShopId == shopId && b.ItemId == itemId && b.BatchNumber == normalizedBatchNumber,
            cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryBatch>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(b => b.Item)
            .Where(b => b.ShopId == shopId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InventoryBatch>> GetByItemIdsAndBatchNumbersAsync(
        Guid shopId,
        IReadOnlyList<Guid> itemIds,
        IReadOnlyList<string> batchNumbers,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(b => b.ShopId == shopId
                && itemIds.Contains(b.ItemId)
                && batchNumbers.Contains(b.BatchNumber))
            .ToListAsync(cancellationToken);

    public async Task<InventoryBatch?> GetByIdWithItemAsync(
        Guid batchId,
        Guid shopId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(b => b.Item)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.ShopId == shopId, cancellationToken);

    public async Task<IReadOnlyList<InventoryBatch>> GetAvailableByBarcodeAsync(
        Guid shopId,
        string barcode,
        CancellationToken cancellationToken = default)
    {
        var normalizedSearchTerm = barcode.Trim();
        return await DbSet
            .Include(b => b.Item)
            .Where(b => b.ShopId == shopId
                && (b.Item.Barcode == normalizedSearchTerm || EF.Functions.ILike(b.Item.Name, $"%{normalizedSearchTerm}%"))
                && !b.IsVoided
                && b.Quantity > 0)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryBatch>> SearchAvailableByProductNameOrBatchNumberAsync(
        Guid shopId,
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        var normalizedSearchTerm = searchTerm.Trim();
        return await DbSet
            .Include(b => b.Item)
            .Where(b => b.ShopId == shopId
                && (EF.Functions.ILike(b.Item.Name, $"%{normalizedSearchTerm}%")
                    || EF.Functions.ILike(b.BatchNumber, $"%{normalizedSearchTerm}%"))
                && !b.IsVoided
                && b.Quantity > 0)
            .OrderBy(b => b.Item.Name)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<decimal> GetCurrentStockValueByShopAsync(
        Guid shopId,
        CancellationToken cancellationToken = default)
    {
        var activeBatches = await DbSet
            .AsNoTracking()
            .Where(b => b.ShopId == shopId && !b.IsVoided && b.Quantity > 0)
            .ToListAsync(cancellationToken);

        return activeBatches.Sum(b => b.Quantity * b.GetProfitCostPrice());
    }
}
