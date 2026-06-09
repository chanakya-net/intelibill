using System.Reflection;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Unit.Tests.Infrastructure.Repositories;

public sealed class PurchaseOrderRepositoryTests
{
    [Fact]
    public void GetByShopQuery_TranslatesOpenWorkFirstSortForPostgres()
    {
        using var context = CreateContext();

        var shopId = Guid.NewGuid();
        var query = context.PurchaseOrders
            .Include(po => po.Lines)
            .Where(po => po.ShopId == shopId)
            .OrderBy(po => po.Status == PurchaseOrderStatus.Draft ? 0 : 99)
            .ThenByDescending(po => po.CreatedAt)
            .ThenByDescending(po => po.Id)
            .Skip(0)
            .Take(20);

        var sql = query.ToQueryString();

        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("created_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPendingPurchaseOrderAlertsAsync_ReturnsOnlyPlacedAndPartiallyReceivedOrders()
    {
        await using var context = await CreateContextAsync();

        var actorId = Guid.NewGuid();
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var item = Item.Create(shop.Id, "Rice", null, "kg", "ITEM-001", true, actorId);
        var supplierId = Guid.NewGuid();

        var pendingOrders = new[]
        {
            CreatePlacedPurchaseOrder(shop.Id, item.Id, supplierId, "PO-001", new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero)),
            CreatePartiallyReceivedPurchaseOrder(shop.Id, item.Id, supplierId, "PO-002", new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero)),
            CreatePlacedPurchaseOrder(shop.Id, item.Id, supplierId, "PO-003", new DateTimeOffset(2026, 6, 3, 9, 0, 0, TimeSpan.Zero)),
            CreatePartiallyReceivedPurchaseOrder(shop.Id, item.Id, supplierId, "PO-004", new DateTimeOffset(2026, 6, 4, 9, 0, 0, TimeSpan.Zero)),
            CreatePlacedPurchaseOrder(shop.Id, item.Id, supplierId, "PO-005", new DateTimeOffset(2026, 6, 5, 9, 0, 0, TimeSpan.Zero)),
            CreatePartiallyReceivedPurchaseOrder(shop.Id, item.Id, supplierId, "PO-006", new DateTimeOffset(2026, 6, 6, 9, 0, 0, TimeSpan.Zero)),
        };
        var excludedOrders = new[]
        {
            CreateClosedPurchaseOrder(shop.Id, item.Id, supplierId, "PO-007", new DateTimeOffset(2026, 6, 8, 9, 0, 0, TimeSpan.Zero)),
            CreateCancelledPurchaseOrder(shop.Id, item.Id, supplierId, "PO-008", new DateTimeOffset(2026, 6, 7, 9, 0, 0, TimeSpan.Zero)),
        };

        await context.AddRangeAsync(shop, item);
        await context.AddRangeAsync(pendingOrders);
        await context.AddRangeAsync(excludedOrders);
        await context.SaveChangesAsync();

        var repository = new PurchaseOrderRepository(context);

        var result = await repository.GetPendingPurchaseOrderAlertsAsync(shop.Id, CancellationToken.None);

        Assert.Equal(5, result.Count);
        Assert.Equal("PO-006", result[0].PurchaseOrderNumber);
        Assert.Equal("PO-005", result[1].PurchaseOrderNumber);
        Assert.Equal("PO-004", result[2].PurchaseOrderNumber);
        Assert.Equal("PO-003", result[3].PurchaseOrderNumber);
        Assert.Equal("PO-002", result[4].PurchaseOrderNumber);
        Assert.DoesNotContain(result, alert => alert.PurchaseOrderNumber is "PO-007" or "PO-008");
        Assert.All(result, alert =>
            Assert.Contains(alert.Status, new[] { PurchaseOrderStatus.Placed, PurchaseOrderStatus.PartiallyReceived }));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=intelibill_tests;Username=test;Password=test")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<ApplicationDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static PurchaseOrder CreatePlacedPurchaseOrder(
        Guid shopId,
        Guid itemId,
        Guid supplierId,
        string purchaseOrderNumber,
        DateTimeOffset createdAt)
    {
        var purchaseOrder = PurchaseOrder.CreateDraft(
            shopId,
            purchaseOrderNumber,
            supplierId,
            new DateOnly(2026, 6, 1),
            null,
            null,
            null,
            "Acme Traders",
            null);

        purchaseOrder.AddLine(itemId, "Rice", 10, 12.5m);
        purchaseOrder.Place(supplierId);
        SetCreatedAt(purchaseOrder, createdAt);
        return purchaseOrder;
    }

    private static PurchaseOrder CreatePartiallyReceivedPurchaseOrder(
        Guid shopId,
        Guid itemId,
        Guid supplierId,
        string purchaseOrderNumber,
        DateTimeOffset createdAt)
    {
        var purchaseOrder = CreatePlacedPurchaseOrder(shopId, itemId, supplierId, purchaseOrderNumber, createdAt);
        var line = purchaseOrder.Lines[0];
        purchaseOrder.ApplyReceipt(line.Id, 4);
        SetCreatedAt(purchaseOrder, createdAt);
        return purchaseOrder;
    }

    private static PurchaseOrder CreateCancelledPurchaseOrder(
        Guid shopId,
        Guid itemId,
        Guid supplierId,
        string purchaseOrderNumber,
        DateTimeOffset createdAt)
    {
        var purchaseOrder = PurchaseOrder.CreateDraft(
            shopId,
            purchaseOrderNumber,
            supplierId,
            new DateOnly(2026, 6, 1),
            null,
            null,
            null,
            "Acme Traders",
            null);

        purchaseOrder.AddLine(itemId, "Rice", 10, 12.5m);
        purchaseOrder.Place(supplierId);
        purchaseOrder.Cancel("Order no longer required");
        SetCreatedAt(purchaseOrder, createdAt);
        return purchaseOrder;
    }

    private static PurchaseOrder CreateClosedPurchaseOrder(
        Guid shopId,
        Guid itemId,
        Guid supplierId,
        string purchaseOrderNumber,
        DateTimeOffset createdAt)
    {
        var purchaseOrder = CreatePartiallyReceivedPurchaseOrder(shopId, itemId, supplierId, purchaseOrderNumber, createdAt);
        purchaseOrder.Close(Guid.NewGuid(), "Completed receipt", createdAt);
        SetCreatedAt(purchaseOrder, createdAt);
        return purchaseOrder;
    }

    private static void SetCreatedAt(PurchaseOrder purchaseOrder, DateTimeOffset createdAt)
    {
        typeof(BaseEntity)
            .GetProperty(nameof(BaseEntity.CreatedAt), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(purchaseOrder, createdAt);
    }
}
