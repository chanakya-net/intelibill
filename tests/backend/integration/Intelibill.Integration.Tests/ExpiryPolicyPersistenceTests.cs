using Intelibill.Application.Features.Expiry;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Infrastructure.Services.Expiry;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpiryPolicyPersistenceTests(PostgreSqlTestFixture fixture)
{
    private ApplicationDbContext Db(bool runtime = false) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(runtime ? fixture.RuntimeConnectionString : fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);

    private static ExpiryPolicyService Service(ApplicationDbContext db) => new(new ExpiryPolicyRepository(db), new ShopRepository(db),
        new ExpiryPolicyFence(db), new UnitOfWork(db), new BusinessClock(TimeProvider.System));

    private async Task<(Guid Shop, Guid Owner, Guid Staff, Guid Manager, Guid Item)> SeedAsync()
    {
        await using var db = Db();
        var shop = Shop.Create("Expiry", "address", "city", "state", "560001", null, null, null);
        var users = Enumerable.Range(0, 3).Select(i => User.CreateWithEmail($"expiry-{Guid.NewGuid():N}@example.test", "hash", "Expiry", "Owner")).ToArray();
        var item = Item.Create(shop.Id, "Lot item", null, "pcs", Guid.NewGuid().ToString(), true, users[0].Id);
        db.AddRange(shop, users[0], users[1], users[2], item,
            ShopMembership.Create(shop.Id, users[0].Id, ShopRole.Owner, false),
            ShopMembership.Create(shop.Id, users[1].Id, ShopRole.Staff, false),
            ShopMembership.Create(shop.Id, users[2].Id, ShopRole.Manager, false));
        await new ExpiryPolicyRepository(db).InitializeAsync(shop.Id, users[0].Id, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();
        return (shop.Id, users[0].Id, users[1].Id, users[2].Id, item.Id);
    }

    [Fact]
    public async Task Revisions_DefaultOverrideResetAndStaleWrite_AreRetained()
    {
        var seed = await SeedAsync();
        await using var db = Db();
        var service = Service(db);
        var original = await service.ReadAsync(seed.Shop, seed.Owner, seed.Item, default);
        Assert.False(original.IsError);
        Assert.Equal(1, original.Value.CatalogVersion);
        Assert.Equal(ExpiryMode.Ignore, original.Value.EffectivePolicy.Mode);
        var changed = await service.UpdateAsync(seed.Shop, seed.Owner, null, new(1, new(ExpiryMode.Block, true, true, 7, false), "required date"), default);
        Assert.False(changed.IsError);
        var overridden = await service.UpdateAsync(seed.Shop, seed.Owner, seed.Item, new(2, new(ExpiryMode.Warn, false, false, 0, true), "item exception"), default);
        Assert.False(overridden.IsError);
        Assert.Equal(3, overridden.Value.EffectivePolicy.Version);
        var reset = await service.UpdateAsync(seed.Shop, seed.Owner, seed.Item, new(3, null, "inherit again"), default);
        Assert.False(reset.IsError);
        Assert.Equal(4, reset.Value.CatalogVersion);
        Assert.Null(reset.Value.OverridePolicy);
        Assert.Equal(2, reset.Value.EffectivePolicy.Version);
        var stale = await service.UpdateAsync(seed.Shop, seed.Owner, null, new(2, new(ExpiryMode.Ignore, false, true, 7, false), "stale"), default);
        Assert.Equal("Expiry.PolicyVersionConflict", stale.FirstError.Code);
        Assert.Equal(4, await db.ExpiryPolicyRevisions.CountAsync(x => x.ShopId == seed.Shop));
        var at = await db.ExpiryPolicyRevisions.Where(x => x.ShopId == seed.Shop && x.CatalogVersion == 2).Select(x => x.EffectiveAt).SingleAsync();
        var history = await new ExpiryPolicyRepository(db).GetEffectiveAsync(seed.Shop, seed.Item, at, default);
        Assert.True(history.HistoryKnown);
        Assert.Equal(ExpiryMode.Block, history.Policy!.Mode);
    }

    [Fact]
    public async Task AuthorityAndTenant_RefusalsDoNotAppend()
    {
        var seed = await SeedAsync();
        var other = await SeedAsync();
        await using var db = Db();
        foreach (var actor in new[] { seed.Staff, seed.Manager, other.Owner })
        {
            var result = await Service(db).UpdateAsync(seed.Shop, actor, null, new(1, new(ExpiryMode.Block, true, true, 7, false), "forbidden"), default);
            Assert.Equal("Expiry.OwnerRequired", result.FirstError.Code);
        }
        var cross = await Service(db).UpdateAsync(seed.Shop, seed.Owner, other.Item, new(1, new(ExpiryMode.Block, true, true, 7, false), "other item"), default);
        Assert.True(cross.IsError);
        Assert.Equal(1, await db.ExpiryPolicyRevisions.CountAsync(x => x.ShopId == seed.Shop));
    }

    [Fact]
    public async Task MissingHistory_IsUnknownAndNeverSynthesizesIgnore()
    {
        var seed = await SeedAsync();
        await using var db = Db();
        var repo = new ExpiryPolicyRepository(db);
        Assert.False((await repo.GetEffectiveAsync(Guid.NewGuid(), seed.Item, null, default)).HistoryKnown);
        Assert.False((await repo.GetEffectiveAsync(seed.Shop, seed.Item, DateTimeOffset.UnixEpoch, default)).HistoryKnown);
    }

    [Fact]
    public async Task HistoricalLookup_MissingCurrentDefaultRevision_CannotCertifySurvivingBaseline()
    {
        var seed = await SeedAsync();
        await using var db = Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Reproduce a damaged retained dataset as the migrator: baseline 1 survives, but the
        // state records a default edit 2 whose full settings/effective event were lost.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = 'replica'");
        await db.Database.ExecuteSqlAsync($"UPDATE expiry_policy_revisions SET pre_feature_baseline = true, actor_user_id = NULL WHERE shop_id = {seed.Shop} AND catalog_version = 1");
        await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = 'origin'");
        await db.Database.ExecuteSqlAsync($"UPDATE shop_expiry_states SET pre_feature_baseline = true, catalog_version = 2, default_policy_version = 2 WHERE shop_id = {seed.Shop}");
        var absentEventAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var repo = new ExpiryPolicyRepository(db);
        foreach (var asOf in new DateTimeOffset?[] { null, absentEventAt.AddMinutes(1) })
        {
            var result = await repo.GetEffectiveAsync(seed.Shop, seed.Item, asOf, default);
            Assert.False(result.HistoryKnown);
            Assert.False(result.IsPreFeatureBaseline);
            Assert.Null(result.Policy);
        }
        var bulk = await repo.GetEffectiveForItemsAsync(seed.Shop, [seed.Item], default);
        Assert.False(bulk[seed.Item].HistoryKnown);
        Assert.Null(bulk[seed.Item].Policy);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task HistoricalLookup_ValidCurrentDefault_PreservesEarlierPolicyAndDateCatalogVersions()
    {
        var seed = await SeedAsync();
        await using var db = Db();
        var baselineAt = await db.ExpiryPolicyRevisions.Where(x => x.ShopId == seed.Shop && x.CatalogVersion == 1).Select(x => x.EffectiveAt).SingleAsync();
        var changed = await Service(db).UpdateAsync(seed.Shop, seed.Owner, null,
            new(1, new(ExpiryMode.Block, true, true, 7, false), "require dates"), default);
        Assert.False(changed.IsError);
        // Date revisions advance the catalog without creating a default-policy revision.
        await db.Database.ExecuteSqlAsync($"UPDATE shop_expiry_states SET catalog_version = 3 WHERE shop_id = {seed.Shop}");
        var repo = new ExpiryPolicyRepository(db);
        var historical = await repo.GetEffectiveAsync(seed.Shop, seed.Item, baselineAt, default);
        Assert.True(historical.HistoryKnown);
        Assert.Equal(ExpiryMode.Ignore, historical.Policy!.Mode);
        Assert.Equal(1, historical.Policy.Version);
        var current = await repo.GetEffectiveAsync(seed.Shop, seed.Item, null, default);
        Assert.True(current.HistoryKnown);
        Assert.Equal(ExpiryMode.Block, current.Policy!.Mode);
        Assert.Equal(2, current.Policy.Version);
        Assert.Equal(3, current.CatalogVersion);
    }

    [Fact]
    public async Task RestrictedRole_SeesOnlyScopedRowsAndCannotMutateFacts()
    {
        var seed = await SeedAsync();
        var other = await SeedAsync();
        await using var db = Db(runtime: true);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {seed.Shop.ToString()}, true)");
        Assert.Equal(1, await db.ShopExpiryStates.CountAsync());
        Assert.Equal(0, await db.ExpiryPolicyRevisions.CountAsync(x => x.ShopId == other.Shop));
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE expiry_policy_revisions SET reason = 'tampered' WHERE shop_id = {seed.Shop}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Fence_RequiresTransactionAndRestoresBudgetOnTimeout()
    {
        var seed = await SeedAsync();
        await using var holder = Db();
        await using var waiter = Db();
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => new ExpiryPolicyFence(waiter).AcquireAsync(seed.Shop, false, default));
        Assert.Contains("transaction", missing.Message, StringComparison.OrdinalIgnoreCase);
        await using var first = await holder.Database.BeginTransactionAsync();
        Assert.False((await new ExpiryPolicyFence(holder).AcquireAsync(seed.Shop, true, default)).IsError);
        await using var second = await waiter.Database.BeginTransactionAsync();
        await waiter.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {"19s"}, true)");
        var result = await new ExpiryPolicyFence(waiter).AcquireAsync(seed.Shop, false, default);
        Assert.Equal("Expiry.PolicyBusy", result.FirstError.Code);
        var timeout = (await waiter.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").ToListAsync()).Single();
        Assert.Equal("19s", timeout);
        await first.RollbackAsync();
        Assert.False((await new ExpiryPolicyFence(waiter).AcquireAsync(seed.Shop, false, default)).IsError);
        var restored = (await waiter.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").ToListAsync()).Single();
        Assert.Equal("19s", restored);
    }

    [Fact]
    public async Task RevisionCompositeForeignKey_RejectsCrossShopItem()
    {
        var seed = await SeedAsync();
        var other = await SeedAsync();
        await using var db = Db();
        db.ExpiryPolicyRevisions.Add(ExpiryPolicyRevision.Create(seed.Shop, other.Item, 2, ExpiryPolicy.Default with { Version = 2 }, seed.Owner, "cross tenant", DateTimeOffset.UtcNow));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Readiness_CountsPersistedPartialQuarantineAndPagesWithoutHidingPhysicalStock()
    {
        var seed = await SeedAsync();
        await using var db = Db();
        var batch = InventoryBatch.Create(seed.Shop, seed.Item, "held", 10, 1, 2, 2, 0, false, new DateOnly(2030, 1, 1), null, null, seed.Owner).Value;
        db.InventoryBatches.Add(batch);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE inventory_batches SET quarantined_quantity = 3 WHERE id = {batch.Id}");
        var result = await Service(db).ReadinessAsync(seed.Shop, seed.Manager, 1, 1, default);
        Assert.False(result.IsError);
        Assert.Equal(10m, result.Value.Results.PhysicalQuantity);
        Assert.Equal(3m, result.Value.Results.QuarantinedQuantity);
        Assert.Equal(1, result.Value.Results.QuarantinedLots);
        Assert.Single(result.Value.Results.Items);
        var staff = await Service(db).ReadinessAsync(seed.Shop, seed.Staff, 1, 1, default);
        Assert.Equal("Expiry.ManagerRequired", staff.FirstError.Code);
    }

    [Fact]
    public async Task Upgrade_SeedsKnownBaselineWithoutChangingHistoricalStockOrDates()
    {
        var name = $"expiry_upgrade_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(fixture.MigratorConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(fixture.MigratorConnectionString) { Database = name, Pooling = false }.ConnectionString;
        try
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);
            await db.GetService<IMigrator>().MigrateAsync("20261108130102_AddTillReconciliationEvidence");
            var shop = Shop.Create("Old shop", "address", "city", "state", "560001", null, null, null);
            var user = User.CreateWithEmail($"upgrade-{Guid.NewGuid():N}@example.test", "hash", "Owner", "Name");
            var item = Item.Create(shop.Id, "Historical item", null, "pcs", Guid.NewGuid().ToString(), true, user.Id);
            db.AddRange(shop, user, item);
            await db.SaveChangesAsync();
            var batchId = Guid.NewGuid();
            var expiry = new DateOnly(2020, 1, 1);
            var manufacturing = new DateOnly(2021, 1, 1); // Existing invalid dates must be reported, never rewritten.
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO inventory_batches (id, shop_id, item_id, batch_number, quantity, original_quantity, cost_price, mrp, sales_price,
                    tax_rate_percent, tax_included, purchase_tax_included, expiry_date, manufacturing_date, is_voided, created_by, created_at)
                VALUES ({batchId}, {shop.Id}, {item.Id}, 'historic', 10, 10, 1, 2, 2, 0, false, false, {expiry}, {manufacturing}, false, {user.Id}, {DateTimeOffset.UtcNow})
                """);
            await db.GetService<IMigrator>().MigrateAsync();
            var createdAt = await db.Shops.Where(x => x.Id == shop.Id).Select(x => x.CreatedAt).SingleAsync();
            var policy = await new ExpiryPolicyRepository(db).GetEffectiveAsync(shop.Id, item.Id, createdAt, default);
            Assert.True(policy.HistoryKnown);
            Assert.True(policy.IsPreFeatureBaseline);
            Assert.Equal(ExpiryMode.Ignore, policy.Policy!.Mode);
            var batch = await db.InventoryBatches.SingleAsync(x => x.Id == batchId);
            Assert.Equal(10m, batch.Quantity);
            Assert.Equal(0m, batch.QuarantinedQuantity);
            Assert.Equal(expiry, batch.ExpiryDate);
            Assert.Equal(manufacturing, batch.ManufacturingDate);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void Migration_RefusesDestructiveDown()
    {
        var migration = new Intelibill.Infrastructure.Migrations.AddExpiryPolicies();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var down = migration.GetType().GetMethod("Down", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => down.Invoke(migration, [builder]));
        Assert.IsType<NotSupportedException>(error.InnerException);
        Assert.Empty(builder.Operations);
    }

    [Fact]
    public async Task ConcurrentUpdates_OneWinsAndOtherConflicts()
    {
        var seed = await SeedAsync();
        async Task<string> ChangeAsync(string reason)
        {
            await using var db = Db();
            var result = await Service(db).UpdateAsync(seed.Shop, seed.Owner, null, new(1, new(ExpiryMode.Block, true, true, 7, false), reason), default);
            return result.IsError ? result.FirstError.Code : "ok";
        }
        var outcomes = await Task.WhenAll(ChangeAsync("one"), ChangeAsync("two"));
        Assert.Single(outcomes, x => x == "ok");
        Assert.Single(outcomes, x => x == "Expiry.PolicyVersionConflict");
        await using var db = Db();
        Assert.Equal(2, await db.ExpiryPolicyRevisions.CountAsync(x => x.ShopId == seed.Shop));
    }
}
