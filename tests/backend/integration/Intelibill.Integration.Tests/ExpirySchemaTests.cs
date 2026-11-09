using System.Text.Json;
using Intelibill.Application.Features.Expiry;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Migrations;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Infrastructure.Services.Expiry;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpirySchemaTests(PostgreSqlTestFixture fixture)
{
    private static readonly string[] NewTables = ["shop_expiry_states", "expiry_policy_revisions", "batch_expiry_revisions", "offline_expiry_evidence", "batch_disposition_changes"];
    private ApplicationDbContext Db(string? connection = null) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection ?? fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);

    private async Task<(Guid Shop, Guid Actor, Guid Item, Guid Batch, Guid Sale)> SeedAsync()
    {
        await using var db = Db();
        var shop = Shop.Create("Expiry schema", "address", "city", "state", "560001", null, null, null);
        var actor = User.CreateWithEmail($"expiry-schema-{Guid.NewGuid():N}@example.test", "hash", "Schema", "Owner");
        var item = Item.Create(shop.Id, "Historical goods", null, "pcs", Guid.NewGuid().ToString(), true, actor.Id);
        var batch = InventoryBatch.Create(shop.Id, item.Id, "dated", 10m, 1m, 2m, 2m, 0m, false, new DateOnly(2020, 1, 1), null, null, actor.Id).Value;
        var undated = InventoryBatch.Create(shop.Id, item.Id, "undated", 5m, 1m, 2m, 2m, 0m, false, null, null, null, actor.Id).Value;
        var inventory = Inventory.Create(shop.Id, item.Id, 15m, 0m, 100m, actor.Id).Value;
        var now = DateTimeOffset.UtcNow;
        var sale = Sale.Record(shop.Id, actor.Id, Guid.NewGuid().ToString(), "retained-hash", Guid.NewGuid().ToString(),
            [new(shop.Id, SaleLineType.Goods, item.Id, batch.Id, null, item.Name, item.Barcode, 1m, 1m, 2m, 2m, 0m, false, false)],
            null, "Historical buyer", null, PaymentMethod.Cash, 2m, 0m, now, BusinessDay.InDefaultZone(now)).Value;
        db.AddRange(shop, actor, item, batch, undated, inventory, sale, ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, false));
        await new ExpiryPolicyRepository(db).InitializeAsync(shop.Id, actor.Id, now, default);
        await db.SaveChangesAsync();
        return (shop.Id, actor.Id, item.Id, batch.Id, sale.Id);
    }

    [Fact]
    public async Task Audit_IsReadOnlyExplicitShopAndMatchesReadinessIncludingOverrideBoundaryAndReset()
    {
        var own = await SeedAsync();
        var foreign = await SeedAsync();
        await using var db = Db();
        await QuarantineFixture.SeedAsync(db, own.Batch, 2m);
        var service = new ExpiryPolicyService(new ExpiryPolicyRepository(db), new ShopRepository(db), new ExpiryPolicyFence(db), new UnitOfWork(db), new BusinessClock(TimeProvider.System));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "scripts", "db"))) root = root.Parent;
        Assert.NotNull(root);
        var script = Path.Combine(root.FullName, "scripts", "db", "audit-expiry-readiness.sql");
        Assert.True(File.Exists(script), "The tenant-scoped, read-only operator audit must be shipped.");
        var sql = (await File.ReadAllTextAsync(script)).Replace(":'shop_id'", "@shop", StringComparison.Ordinal)
            .Replace(":'business_date'", "@day", StringComparison.Ordinal);
        foreach (var inclusive in new[] { false, true })
        {
            var current = await service.ReadAsync(own.Shop, own.Actor, own.Item, default);
            var changed = await service.UpdateAsync(own.Shop, own.Actor, own.Item,
                new(current.Value.CatalogVersion, inclusive ? null : new(ExpiryMode.Ignore, false, false, 7, false), "Boundary override then inherit"), default);
            Assert.False(changed.IsError);
            var readiness = await service.ReadinessAsync(own.Shop, own.Actor, 1, 100, default);
            Assert.False(readiness.IsError);
            var day = readiness.Value.BusinessDate;
            await db.Database.ExecuteSqlAsync($"UPDATE inventory_batches SET expiry_date = {day} WHERE id = {own.Batch}");
            readiness = await service.ReadinessAsync(own.Shop, own.Actor, 1, 100, default);
            await using var connection = new NpgsqlConnection(fixture.RuntimeConnectionString);
            await connection.OpenAsync();
            await using (var scope = new NpgsqlCommand("select set_config('app.active_shop_id', @shop, false)", connection))
            { scope.Parameters.AddWithValue("shop", own.Shop.ToString()); await scope.ExecuteNonQueryAsync(); }
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction)) await readOnly.ExecuteNonQueryAsync();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("shop", own.Shop); command.Parameters.AddWithValue("day", day);
            using var document = JsonDocument.Parse(Assert.IsType<string>(await command.ExecuteScalarAsync()));
            var audit = document.RootElement;
            Assert.Equal(own.Shop, audit.GetProperty("shopId").GetGuid());
            Assert.Equal(readiness.Value.CatalogVersion, audit.GetProperty("catalogVersion").GetInt64());
            var totals = audit.GetProperty("results"); var expected = readiness.Value.Results;
            Assert.Equal(expected.TotalLots, totals.GetProperty("totalLots").GetInt32());
            Assert.Equal(expected.MissingDateLots, totals.GetProperty("missingDateLots").GetInt32());
            Assert.Equal(expected.ExpiredLots, totals.GetProperty("expiredLots").GetInt32());
            Assert.Equal(inclusive ? 0 : 1, expected.ExpiredLots);
            Assert.Equal(expected.QuarantinedLots, totals.GetProperty("quarantinedLots").GetInt32());
            Assert.Equal(expected.PhysicalQuantity, totals.GetProperty("physicalQuantity").GetDecimal());
            Assert.Equal(expected.QuarantinedQuantity, totals.GetProperty("quarantinedQuantity").GetDecimal());
            Assert.Equal(expected.Items.Select(x => x.BatchId).Order(), totals.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("batchId").GetGuid()).Order());
            Assert.DoesNotContain(foreign.Batch, totals.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("batchId").GetGuid()));
            command.Parameters["shop"].Value = foreign.Shop;
            Assert.Null(await command.ExecuteScalarAsync()); // Explicit input cannot bypass restricted-role RLS.
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task PopulatedPreFeatureUpgrade_PreservesStockDatesAmountsAndInvoiceJsonAcrossAllFourMigrations()
    {
        var seed = await SeedAsync();
        await using var source = new NpgsqlConnection(fixture.MigratorConnectionString);
        await source.OpenAsync();
        // Persisted historic inconsistency is deliberately retained; migrations must not repair it.
        await using (var history = new NpgsqlCommand("update inventory_batches set manufacturing_date=date '2021-01-01' where id=@id; update sales set invoice_document=' {\"version\":2,\"number\":\"A/2627/000001\",\"seller\":{\"name\":\"Frozen seller\"},\"total\":2}'::jsonb where id=@sale", source))
        { history.Parameters.AddWithValue("id", seed.Batch); history.Parameters.AddWithValue("sale", seed.Sale); await history.ExecuteNonQueryAsync(); }
        var tables = new[] { "shops", "users", "items", "inventory", "inventory_batches", "sales", "sale_items" };
        var rows = new Dictionary<string, List<string>>();
        foreach (var table in tables)
        {
            await using var command = new NpgsqlCommand($"select row_to_json(t)::text from {table} t where {(table is "shops" or "users" ? "id" : "shop_id")}=@id", source);
            command.Parameters.AddWithValue("id", table == "users" ? seed.Actor : seed.Shop);
            await using var reader = await command.ExecuteReaderAsync(); var snapshots = new List<string>();
            while (await reader.ReadAsync())
            {
                var snapshot = JsonSerializer.Deserialize<SortedDictionary<string, JsonElement>>(reader.GetString(0))!;
                if (table == "inventory_batches") snapshot.Remove("quarantined_quantity");
                if (table == "sales") snapshot.Remove("expiry_decisions");
                snapshots.Add(JsonSerializer.Serialize(snapshot));
            }
            rows[table] = snapshots;
        }
        var name = $"expiry_all_upgrade_{Guid.NewGuid():N}";
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", source)) await create.ExecuteNonQueryAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.MigratorConnectionString) { Database = name, Pooling = false }.ConnectionString;
        try
        {
            await using var db = Db(connectionString);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261108130102_AddTillReconciliationEvidence");
            await using var target = new NpgsqlConnection(connectionString); await target.OpenAsync();
            foreach (var table in tables)
                foreach (var json in rows[table])
                {
                    await using var insert = new NpgsqlCommand($"insert into {table} select * from json_populate_record(null::{table}, @row::json)", target);
                    insert.Parameters.AddWithValue("row", json); await insert.ExecuteNonQueryAsync();
                }
            foreach (var migration in new[] { "20261108182442_AddExpiryPolicies", "20261108184604_AddExpirySaleEvidence", "20261108193354_AddOfflineExpiryEvidence", "20261108210054_AddBatchDispositionEvidence" })
                await migrator.MigrateAsync(migration);
            foreach (var table in tables)
            {
                var added = table == "inventory_batches" ? " - 'quarantined_quantity'" : table == "sales" ? " - 'expiry_decisions'" : "";
                await using var query = new NpgsqlCommand($"select (to_jsonb(t){added})::text from {table} t order by id", target);
                await using var reader = await query.ExecuteReaderAsync(); var actual = new List<string>();
                while (await reader.ReadAsync()) actual.Add(reader.GetString(0));
                Assert.Equal(rows[table].Select(x => JsonSerializer.Serialize(JsonSerializer.Deserialize<SortedDictionary<string, JsonElement>>(x))).Order(),
                    actual.Select(x => JsonSerializer.Serialize(JsonSerializer.Deserialize<SortedDictionary<string, JsonElement>>(x))).Order());
            }
            var policy = await new ExpiryPolicyRepository(db).GetEffectiveAsync(seed.Shop, seed.Item, null, default);
            Assert.True(policy.HistoryKnown); Assert.True(policy.IsPreFeatureBaseline); Assert.Equal(ExpiryPolicy.Default, policy.Policy);
            Assert.All(await db.InventoryBatches.ToListAsync(), x => Assert.Equal(0m, x.QuarantinedQuantity));
            Assert.Empty(await db.BatchExpiryRevisions.ToListAsync()); Assert.Empty(await db.OfflineExpiryEvidence.ToListAsync()); Assert.Empty(await db.BatchDispositionChanges.ToListAsync());
            Assert.Equal(1, await db.ExpiryPolicyRevisions.CountAsync());
            await RestrictedRuntimeRole.GrantAsync(connectionString);
            await using var runtime = new NpgsqlConnection(RestrictedRuntimeRole.ConnectionStringFor(connectionString)); await runtime.OpenAsync();
            foreach (var table in NewTables)
            {
                await using var catalog = new NpgsqlCommand("select relrowsecurity and relforcerowsecurity and exists(select 1 from pg_policy p where p.polrelid=c.oid and p.polqual is not null and p.polwithcheck is not null) from pg_class c where relname=@table", runtime);
                catalog.Parameters.AddWithValue("table", table); Assert.Equal(true, await catalog.ExecuteScalarAsync());
                await using var read = new NpgsqlCommand($"select count(*) from {table}", runtime); Assert.Equal(0L, await read.ExecuteScalarAsync());
            }
        }
        finally { await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", source); await drop.ExecuteNonQueryAsync(); }
    }

    [Fact]
    public async Task EveryNewTable_RestrictedRoleScopesReadsAndRefusesForeignInsert_AndFactsRejectUpdateAndDelete()
    {
        var own = await SeedRetainedFactsAsync(); var foreign = await SeedRetainedFactsAsync();
        await using var admin = new NpgsqlConnection(fixture.MigratorConnectionString); await admin.OpenAsync();
        await using var runtime = new NpgsqlConnection(fixture.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var table in NewTables)
        {
            await using (var scope = new NpgsqlCommand("select set_config('app.active_shop_id', @shop, false)", runtime))
            { scope.Parameters.AddWithValue("shop", own.ToString()); await scope.ExecuteNonQueryAsync(); }
            await using (var query = new NpgsqlCommand($"select count(*) from {table}", runtime)) Assert.Equal(1L, await query.ExecuteScalarAsync());
            await using (var query = new NpgsqlCommand($"select row_to_json(t)::text from {table} t where shop_id=@shop", admin))
            {
                query.Parameters.AddWithValue("shop", foreign); var json = Assert.IsType<string>(await query.ExecuteScalarAsync());
                await using var insert = new NpgsqlCommand($"insert into {table} select * from json_populate_record(null::{table}, @row::json)", runtime);
                insert.Parameters.AddWithValue("row", json);
                var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
            if (table != "shop_expiry_states")
                foreach (var mutation in new[] { $"UPDATE {table} SET shop_id=shop_id", $"DELETE FROM {table}" })
                {
                    await using var command = new NpgsqlCommand(mutation, runtime);
                    var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
                    Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
                }
            await using (var clear = new NpgsqlCommand("select set_config('app.active_shop_id', '', false)", runtime)) await clear.ExecuteNonQueryAsync();
            await using var blank = new NpgsqlCommand($"select count(*) from {table}", runtime); Assert.Equal(0L, await blank.ExecuteScalarAsync());
        }
    }

    private async Task<Guid> SeedRetainedFactsAsync()
    {
        var seed = await SeedAsync(); await using var db = Db();
        await QuarantineFixture.SeedAsync(db, seed.Batch, 2m);
        var batch = await db.InventoryBatches.Include(x => x.Item).SingleAsync(x => x.Id == seed.Batch);
        db.AddRange(BatchExpiryRevision.Create(batch, batch.ExpiryDate, null, seed.Actor, "Retained date confirmation", DateTimeOffset.UtcNow, 2),
            OfflineExpiryEvidence.Capture(Guid.NewGuid(), batch, ExpiryPolicy.Default, 1, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(); return seed.Shop;
    }

    [Theory]
    [InlineData("UPDATE inventory_batches SET quarantined_quantity=-1 WHERE id=@batch")]
    [InlineData("UPDATE inventory_batches SET quarantined_quantity=quantity+1 WHERE id=@batch")]
    [InlineData("UPDATE shop_expiry_states SET catalog_version=0 WHERE shop_id=@shop")]
    [InlineData("UPDATE shop_expiry_states SET default_policy_version=catalog_version+1 WHERE shop_id=@shop")]
    [InlineData("INSERT INTO expiry_policy_revisions SELECT gen_random_uuid(),shop_id,item_id,2,3,false,true,7,false,actor_user_id,'invalid mode',effective_at,false FROM expiry_policy_revisions WHERE shop_id=@shop")]
    [InlineData("INSERT INTO expiry_policy_revisions SELECT gen_random_uuid(),shop_id,item_id,2,0,false,true,366,false,actor_user_id,'invalid warning days',effective_at,false FROM expiry_policy_revisions WHERE shop_id=@shop")]
    [InlineData("INSERT INTO expiry_policy_revisions SELECT gen_random_uuid(),shop_id,item_id,2,0,false,true,-1,false,actor_user_id,'invalid warning days',effective_at,false FROM expiry_policy_revisions WHERE shop_id=@shop")]
    public async Task DatabaseConstraints_RejectInvalidPolicyAndPhysicalSubsetRanges(string sql)
    {
        var seed = await SeedAsync(); await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("shop", seed.Shop); command.Parameters.AddWithValue("batch", seed.Batch);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync()); Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData("batch_expiry_revisions", "{\"previous_batch_version\":0}")]
    [InlineData("batch_expiry_revisions", "{\"catalog_version\":1}")]
    [InlineData("batch_expiry_revisions", "{\"new_manufacturing_date\":\"2021-01-01\",\"new_expiry_date\":\"2020-01-01\"}")]
    [InlineData("offline_expiry_evidence", "{\"catalog_version\":0}")]
    [InlineData("offline_expiry_evidence", "{\"expires_at\":\"2020-01-01T00:00:00Z\"}")]
    [InlineData("batch_disposition_changes", "{\"quantity\":0}")]
    [InlineData("batch_disposition_changes", "{\"source_kind\":\"InventedSource\"}")]
    [InlineData("batch_disposition_changes", "{\"action\":\"Release\"}")]
    public async Task RetainedEvidenceConstraints_RejectInvalidNewFacts(string table, string patch)
    {
        var shop = await SeedRetainedFactsAsync(); await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"insert into {table} select (jsonb_populate_record(null::{table}, to_jsonb(t) || @patch::jsonb || jsonb_build_object('id', gen_random_uuid()))).* from {table} t where shop_id=@shop", connection);
        command.Parameters.AddWithValue("shop", shop); command.Parameters.AddWithValue("patch", patch);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync()); Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData(typeof(AddExpiryPolicies))]
    [InlineData(typeof(AddExpirySaleEvidence))]
    [InlineData(typeof(AddOfflineExpiryEvidence))]
    [InlineData(typeof(AddBatchDispositionEvidence))]
    public void EveryMigration_RefusesDownBeforeEmittingAnyDestructiveOperation(Type type)
    {
        var migration = Assert.IsAssignableFrom<Migration>(Activator.CreateInstance(type));
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var down = type.GetMethod("Down", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => down.Invoke(migration, [builder]));
        Assert.IsType<NotSupportedException>(exception.InnerException); Assert.Empty(builder.Operations);
    }
}
