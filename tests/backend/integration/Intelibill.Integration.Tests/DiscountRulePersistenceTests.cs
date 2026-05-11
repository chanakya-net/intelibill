using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class DiscountRulePersistenceTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public async Task InitializeAsync() => await _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Repository_CanPersistAndRetrieve_DiscountRule()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDiscountRuleRepository>();

        var shopId = await SeedShopAsync(db);
        var rule = CreateRule(shopId, DiscountRuleType.SalePercentage, "Summer Sale", 15m);

        db.DiscountRules.Add(rule);
        await db.SaveChangesAsync();

        var fetched = await repository.GetByIdAsync(rule.Id);
        Assert.NotNull(fetched);
        Assert.Equal(rule.Id, fetched.Id);
        Assert.Equal(shopId, fetched.ShopId);
        Assert.Equal("Summer Sale", fetched.Name);
        Assert.Equal(15m, fetched.Percentage);
        Assert.Equal(DiscountRuleType.SalePercentage, fetched.RuleType);
        Assert.True(fetched.IsActive);
    }

    [Fact]
    public async Task GetActiveByShopAsync_ReturnsOnlyActiveAndWithinWindow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDiscountRuleRepository>();

        var shopId = await SeedShopAsync(db);
        var now = DateTimeOffset.UtcNow;

        var activeRule = CreateRule(shopId, DiscountRuleType.SalePercentage, "Active", 10m);
        var disabledRule = CreateRule(shopId, DiscountRuleType.SalePercentage, "Disabled", 5m);
        disabledRule.Disable("manual", now, Guid.NewGuid());
        var futureRule = CreateRule(shopId, DiscountRuleType.SalePercentage, "Future", 8m,
            startsAt: now.AddDays(1));
        var expiredRule = CreateRule(shopId, DiscountRuleType.SalePercentage, "Expired", 12m,
            startsAt: now.AddDays(-10), endsAt: now.AddDays(-1));

        db.DiscountRules.AddRange(activeRule, disabledRule, futureRule, expiredRule);
        await db.SaveChangesAsync();

        var active = await repository.GetActiveByShopAsync(shopId, now);

        Assert.Contains(active, r => r.Id == activeRule.Id);
        Assert.DoesNotContain(active, r => r.Id == disabledRule.Id);
        Assert.DoesNotContain(active, r => r.Id == futureRule.Id);
        Assert.DoesNotContain(active, r => r.Id == expiredRule.Id);
    }

    [Fact]
    public async Task GetUpcomingByShopAsync_ReturnsOnlyFutureActiveRules()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDiscountRuleRepository>();

        var shopId = await SeedShopAsync(db);
        var now = DateTimeOffset.UtcNow;

        var currentRule = CreateRule(shopId, DiscountRuleType.SalePercentage, "Current", 10m);
        var upcomingRule = CreateRule(shopId, DiscountRuleType.SalePercentage, "Upcoming", 20m,
            startsAt: now.AddDays(3));
        var disabledUpcoming = CreateRule(shopId, DiscountRuleType.SalePercentage, "DisabledUpcoming", 5m,
            startsAt: now.AddDays(5));
        disabledUpcoming.Disable("cancelled", now, Guid.NewGuid());

        db.DiscountRules.AddRange(currentRule, upcomingRule, disabledUpcoming);
        await db.SaveChangesAsync();

        var upcoming = await repository.GetUpcomingByShopAsync(shopId, now);

        Assert.DoesNotContain(upcoming, r => r.Id == currentRule.Id);
        Assert.Contains(upcoming, r => r.Id == upcomingRule.Id);
        Assert.DoesNotContain(upcoming, r => r.Id == disabledUpcoming.Id);
    }

    [Fact]
    public async Task GetActiveByBatchAsync_ReturnsOnlyBatchScopedActiveRules()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDiscountRuleRepository>();

        var shopId = await SeedShopAsync(db);
        var (batchId, _) = await SeedBatchAsync(db, shopId);
        var (otherBatchId, _) = await SeedBatchAsync(db, shopId);
        var now = DateTimeOffset.UtcNow;

        var batchRule = CreateRule(shopId, DiscountRuleType.BatchPercentage, "Batch Promo", 5m,
            inventoryBatchId: batchId);
        var otherBatchRule = CreateRule(shopId, DiscountRuleType.BatchPercentage, "Other Batch", 8m,
            inventoryBatchId: otherBatchId);
        var shopWideRule = CreateRule(shopId, DiscountRuleType.SalePercentage, "Shop Wide", 3m);

        db.DiscountRules.AddRange(batchRule, otherBatchRule, shopWideRule);
        await db.SaveChangesAsync();

        var results = await repository.GetActiveByBatchAsync(shopId, batchId, now);

        Assert.Contains(results, r => r.Id == batchRule.Id);
        Assert.DoesNotContain(results, r => r.Id == otherBatchRule.Id);
        Assert.DoesNotContain(results, r => r.Id == shopWideRule.Id);
    }

    [Fact]
    public async Task GetByShopAsync_DoesNotReturnRulesFromOtherShop()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDiscountRuleRepository>();

        var shop1Id = await SeedShopAsync(db);
        var shop2Id = await SeedShopAsync(db);

        var rule1 = CreateRule(shop1Id, DiscountRuleType.SalePercentage, "Shop1 Rule", 10m);
        var rule2 = CreateRule(shop2Id, DiscountRuleType.SalePercentage, "Shop2 Rule", 20m);

        db.DiscountRules.AddRange(rule1, rule2);
        await db.SaveChangesAsync();

        var shop1Rules = await repository.GetByShopAsync(shop1Id);

        Assert.Contains(shop1Rules, r => r.Id == rule1.Id);
        Assert.DoesNotContain(shop1Rules, r => r.Id == rule2.Id);
    }

    [Fact]
    public async Task SoftDisable_PersistsAuditFields()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDiscountRuleRepository>();

        var shopId = await SeedShopAsync(db);
        var rule = CreateRule(shopId, DiscountRuleType.SalePercentage, "Promo", 10m);
        db.DiscountRules.Add(rule);
        await db.SaveChangesAsync();

        var disabledAt = DateTimeOffset.UtcNow;
        var disabledBy = Guid.NewGuid();
        rule.Disable("Promo ended", disabledAt, disabledBy);
        await db.SaveChangesAsync();

        var fetched = await repository.GetByIdAsync(rule.Id);
        Assert.NotNull(fetched);
        Assert.False(fetched.IsActive);
        Assert.Equal("Promo ended", fetched.DisabledReason);
        Assert.NotNull(fetched.DisabledAt);
    }

    [Fact]
    public async Task VersionLinks_PersistReplacesAndReplacedBy()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDiscountRuleRepository>();

        var shopId = await SeedShopAsync(db);
        var oldRule = CreateRule(shopId, DiscountRuleType.SalePercentage, "V1", 5m);
        var newRule = CreateRule(shopId, DiscountRuleType.SalePercentage, "V2", 8m);

        db.DiscountRules.Add(oldRule);
        db.DiscountRules.Add(newRule);
        await db.SaveChangesAsync();

        oldRule.ReplaceWith(newRule.Id, DateTimeOffset.UtcNow, Guid.NewGuid());
        newRule.MarkAsReplacement(oldRule.Id);
        await db.SaveChangesAsync();

        var fetchedOld = await repository.GetByIdAsync(oldRule.Id);
        var fetchedNew = await repository.GetByIdAsync(newRule.Id);

        Assert.NotNull(fetchedOld);
        Assert.Equal(newRule.Id, fetchedOld.ReplacedByRuleId);
        Assert.False(fetchedOld.IsActive);

        Assert.NotNull(fetchedNew);
        Assert.Equal(oldRule.Id, fetchedNew.ReplacesRuleId);
        Assert.True(fetchedNew.IsActive);
    }

    private static async Task<Guid> SeedShopAsync(ApplicationDbContext db)
    {
        var shop = Shop.Create(
            $"Shop {Guid.NewGuid():N}",
            "1 Main St",
            "City",
            "State",
            "560001",
            null, null, null);
        db.Shops.Add(shop);
        await db.SaveChangesAsync();
        return shop.Id;
    }

    private static async Task<(Guid BatchId, Guid ItemId)> SeedBatchAsync(ApplicationDbContext db, Guid shopId)
    {
        var actorId = Guid.NewGuid();
        var item = Item.Create(shopId, $"Item {Guid.NewGuid():N}", null, "pcs", Guid.NewGuid().ToString("N"), true, actorId);
        var batch = InventoryBatch.Create(
            shopId, item.Id, $"B-{Guid.NewGuid():N}",
            10m, 50m, 80m, 70m, 0m, false, null, null, null, actorId).Value;

        db.Items.Add(item);
        db.InventoryBatches.Add(batch);
        await db.SaveChangesAsync();

        return (batch.Id, item.Id);
    }

    private static DiscountRule CreateRule(
        Guid shopId,
        DiscountRuleType ruleType,
        string name,
        decimal percentage,
        Guid? inventoryBatchId = null,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        decimal? thresholdAmount = null)
    {
        return DiscountRule.Create(
            shopId,
            ruleType,
            name,
            description: null,
            inventoryBatchId: inventoryBatchId,
            percentage: percentage,
            thresholdAmount: ruleType == DiscountRuleType.SaleThresholdPercentage ? (thresholdAmount ?? 100m) : thresholdAmount,
            startsAt: startsAt,
            endsAt: endsAt,
            belowCostConfirmed: false,
            belowCostConfirmationReason: null,
            createdBy: Guid.NewGuid()).Value;
    }
}
