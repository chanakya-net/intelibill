using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Infrastructure.Services.Expiry;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SaleExpiryGuardTests(PostgreSqlTestFixture fixture)
{
    private ApplicationDbContext Db(bool runtime = false) => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(runtime ? fixture.RuntimeConnectionString : fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);
    private static readonly DateOnly Date = new(2026, 10, 22);
    private static SaleExpiryGuard Guard(ApplicationDbContext db) => new(new ExpiryPolicyRepository(db), new ShopRepository(db), new BusinessClock(TimeProvider.System));
    private async Task<(Guid Shop, Guid Owner, Guid Staff, Guid Batch)> Seed(ExpiryMode mode, bool required = false, bool overrides = false, DateOnly? expiry = null)
    {
        await using var db = Db();
        var shop = Shop.Create("Expiry sale", "address", "city", "state", "560001", null, null, null);
        var owner = User.CreateWithEmail($"expiry-sale-{Guid.NewGuid():N}@example.test", "hash", "Owner", "Name");
        var staff = User.CreateWithEmail($"expiry-sale-{Guid.NewGuid():N}@example.test", "hash", "Staff", "Name");
        var item = Item.Create(shop.Id, "Expiry product", null, "pcs", Guid.NewGuid().ToString(), true, owner.Id);
        var batch = InventoryBatch.Create(shop.Id, item.Id, "lot", 10, 1, 2, 2, 0, false, expiry, null, null, owner.Id).Value;
        db.AddRange(shop, owner, staff, item, batch, ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, false), ShopMembership.Create(shop.Id, staff.Id, ShopRole.Staff, false));
        var repo = new ExpiryPolicyRepository(db);
        await repo.InitializeAsync(shop.Id, owner.Id, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();
        var service = new ExpiryPolicyService(repo, new ShopRepository(db), new ExpiryPolicyFence(db), new UnitOfWork(db), new BusinessClock(TimeProvider.System));
        var result = await service.UpdateAsync(shop.Id, owner.Id, null, new(1, new(mode, required, true, 7, overrides), "sale policy"), default);
        Assert.False(result.IsError);
        return (shop.Id, owner.Id, staff.Id, batch.Id);
    }
    private static async Task<ValidatedSaleLine> Line(ApplicationDbContext db, Guid batchId, decimal quantity = 1)
    {
        var batch = await db.InventoryBatches.Include(x => x.Item).SingleAsync(x => x.Id == batchId);
        return new(new RecordSaleItemCommand(batch.Item.Barcode, batch.BatchNumber, batch.Item.Name, quantity, 1, 2, 2, 0, false, batch.Id), batch.Item, batch, false);
    }
    [Theory]
    [InlineData(ExpiryMode.Block, false, "Expiry.Expired")]
    [InlineData(ExpiryMode.Warn, false, "Expiry.OverrideDisabled")]
    [InlineData(ExpiryMode.Ignore, true, "Expiry.RequiredDateMissing")]
    public async Task HardBlocks_RejectCraftedBatchIdsAndApprovals(ExpiryMode mode, bool required, string code)
    {
        var seed = await Seed(mode, required, expiry: required ? null : Date.AddDays(-1));
        await using var db = Db();
        var line = await Line(db, seed.Batch);
        var result = await Guard(db).AuthorizeAsync(seed.Shop, seed.Owner, Date, [line], [new(seed.Batch, 1, line.Batch!.Version, 2, 2, "override")], default);
        Assert.True(result.IsError);
        Assert.Equal(code, result.FirstError.Code);
        Assert.Equal(10m, (await db.InventoryBatches.SingleAsync(x => x.Id == seed.Batch)).Quantity);
        Assert.Empty(await db.Sales.Where(x => x.ShopId == seed.Shop).ToListAsync());
    }
    [Fact]
    public async Task BusinessBoundary_PreviewUsesExplicitBusinessDate()
    {
        var seed = await Seed(ExpiryMode.Block, expiry: Date.AddDays(-1));
        await using var db = Db();
        var line = await Line(db, seed.Batch);
        var before = await Guard(db).EvaluateAsync(seed.Shop, Date.AddDays(-1), [line], default);
        var after = await Guard(db).EvaluateAsync(seed.Shop, Date, [line], default);
        Assert.True(before.Single().Decision.Allowed);
        Assert.False(after.Single().Decision.Allowed);
        var clock = new BusinessClock(TimeProvider.System);
        Assert.Equal(Date.AddDays(-1), clock.GetBusinessDate(DateTimeOffset.Parse("2026-10-21T18:29:59Z", System.Globalization.CultureInfo.InvariantCulture), "Asia/Kolkata"));
        Assert.Equal(Date, clock.GetBusinessDate(DateTimeOffset.Parse("2026-10-21T18:30:00Z", System.Globalization.CultureInfo.InvariantCulture), "Asia/Kolkata"));
    }
    [Fact]
    public async Task WarningApproval_RequiresLiveRoleReasonVersionsAndSummedQuantity()
    {
        var seed = await Seed(ExpiryMode.Warn, overrides: true, expiry: Date.AddDays(-1));
        await using var db = Db();
        var line = await Line(db, seed.Batch);
        var approval = new ExpiryApprovalRequest(seed.Batch, 2, line.Batch!.Version, 2, 2, "inspected by manager");
        var guard = Guard(db);
        foreach (var bad in new[] { approval with { Quantity = 1 }, approval with { BatchVersion = 0 }, approval with { PolicyVersion = 1 }, approval with { CatalogVersion = 1 }, approval with { Reason = " " } })
            Assert.True((await guard.AuthorizeAsync(seed.Shop, seed.Owner, Date, [line, line], [bad], default)).IsError);
        Assert.True((await guard.AuthorizeAsync(seed.Shop, seed.Staff, Date, [line, line], [approval], default)).IsError);
        Assert.True((await guard.AuthorizeAsync(seed.Shop, seed.Owner, Date, [line, line], [approval, approval], default)).IsError);
        Assert.True((await guard.AuthorizeAsync(seed.Shop, seed.Owner, Date, [line, line], null, default)).IsError);
        var good = await guard.AuthorizeAsync(seed.Shop, seed.Owner, Date, [line, line], [approval], default);
        Assert.False(good.IsError);
        Assert.Equal(2, good.Value.Count);
        Assert.All(good.Value, x => { Assert.Equal(seed.Owner, x.ApprovedBy); Assert.Equal(approval.Reason, x.ApprovalReason); Assert.Equal(1m, x.Quantity); });
    }
    [Fact]
    public async Task ReceiptDates_RequireDateAndValidPairAndAppendDateOnlyHistory()
    {
        var seed = await Seed(ExpiryMode.Block, required: true, expiry: Date.AddDays(10));
        await using var db = Db();
        var service = new BatchExpiryRevisionService(new ExpiryPolicyRepository(db), new BatchExpiryRevisionRepository(db), new ShopRepository(db), new BusinessClock(TimeProvider.System));
        await using var tx = await db.Database.BeginTransactionAsync();
        Assert.False((await new ExpiryPolicyFence(db).AcquireAsync(seed.Shop, true, default)).IsError);
        var line = await Line(db, seed.Batch);
        Assert.Equal("Expiry.RequiredDateMissing", (await service.ReviseAsync(line.Batch!, null, null, seed.Owner, "remove", default)).FirstError.Code);
        Assert.Equal("Expiry.InvalidDateOrder", (await service.ReviseAsync(line.Batch!, Date, Date.AddDays(1), seed.Owner, "wrong", default)).FirstError.Code);
        Assert.False((await service.ReviseAsync(line.Batch!, Date.AddDays(20), null, seed.Owner, "correct label", default)).IsError);
        await db.SaveChangesAsync();
        Assert.Single(await db.BatchExpiryRevisions.Where(x => x.ShopId == seed.Shop).ToListAsync());
        Assert.Equal(3, (await new ExpiryPolicyRepository(db).GetStateAsync(seed.Shop, default))!.CatalogVersion);
    }
    [Fact]
    public async Task WarningApproval_LiveManagerIsRecordedAsActualApprover()
    {
        var seed = await Seed(ExpiryMode.Warn, overrides: true, expiry: Date.AddDays(-1));
        await using var db = Db();
        await db.Database.ExecuteSqlAsync($"UPDATE shop_memberships SET role = {(int)ShopRole.Manager} WHERE shop_id = {seed.Shop} AND user_id = {seed.Staff}");
        var line = await Line(db, seed.Batch);
        var result = await Guard(db).AuthorizeAsync(seed.Shop, seed.Staff, Date, [line], [new(seed.Batch, 1, line.Batch!.Version, 2, 2, "Manager inspection")], default);
        Assert.False(result.IsError);
        Assert.Equal(seed.Staff, result.Value.Single().ApprovedBy);
    }
    [Fact]
    public async Task DateHistory_UsesTenantRlsAndCompositeBatchForeignKey()
    {
        var seed = await Seed(ExpiryMode.Ignore, expiry: Date);
        var other = await Seed(ExpiryMode.Ignore, expiry: Date);
        await using (var db = Db())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            Assert.False((await new ExpiryPolicyFence(db).AcquireAsync(seed.Shop, true, default)).IsError);
            var line = await Line(db, seed.Batch);
            var service = new BatchExpiryRevisionService(new ExpiryPolicyRepository(db), new BatchExpiryRevisionRepository(db), new ShopRepository(db), new BusinessClock(TimeProvider.System));
            Assert.False((await service.ReviseAsync(line.Batch!, Date.AddDays(1), null, seed.Owner, "label correction", default)).IsError);
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        await using (var scoped = Db(runtime: true))
        {
            await using var tx = await scoped.Database.BeginTransactionAsync();
            await scoped.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {other.Shop.ToString()}, true)");
            Assert.Empty(await scoped.BatchExpiryRevisions.Where(x => x.ShopId == seed.Shop).ToListAsync());
            await scoped.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {seed.Shop.ToString()}, true)");
            Assert.Single(await scoped.BatchExpiryRevisions.ToListAsync());
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => scoped.Database.ExecuteSqlAsync($"UPDATE batch_expiry_revisions SET reason = 'tampered' WHERE shop_id = {seed.Shop}"));
            Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        await using var invalid = Db();
        var foreignBatch = (await Line(invalid, other.Batch)).Batch!;
        var revision = BatchExpiryRevision.Create(foreignBatch, Date.AddDays(1), null, seed.Owner, "wrong tenant", DateTimeOffset.UtcNow, 4);
        typeof(BatchExpiryRevision).GetProperty(nameof(BatchExpiryRevision.ShopId))!.SetValue(revision, seed.Shop);
        invalid.BatchExpiryRevisions.Add(revision);
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
        Assert.Equal(Npgsql.PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<Npgsql.PostgresException>(failure.InnerException).SqlState);
    }
    [Fact]
    public void EvidenceMigration_RefusesDestructiveDown()
    {
        var migration = new Intelibill.Infrastructure.Migrations.AddExpirySaleEvidence();
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var down = migration.GetType().GetMethod("Down", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => down.Invoke(migration, [builder]));
        Assert.IsType<NotSupportedException>(exception.InnerException); Assert.Empty(builder.Operations);
    }
    private sealed class PolicyReadCounter : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public void Reset() => Reads = 0;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("shop_expiry_states", StringComparison.Ordinal) || command.CommandText.Contains("expiry_policy_revisions", StringComparison.Ordinal)) Reads++;
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task PolicyResolution_ReusesDistinctItemFactsForEvaluationAndEvidenceWithoutCrossOperationCache()
    {
        var seed = await Seed(ExpiryMode.Warn, overrides: true, expiry: Date.AddDays(-1));
        var batches = new List<Guid> { seed.Batch };
        Guid overriddenItem;
        await using (var seedDb = Db())
        {
            var original = await seedDb.InventoryBatches.SingleAsync(x => x.Id == seed.Batch);
            for (var index = 0; index < 5; index++)
            {
                var lot = InventoryBatch.Create(seed.Shop, original.ItemId, $"query-budget-{index}", 10, 1, 2, 2, 0, false, index % 2 == 0 ? Date.AddDays(-1) : Date.AddDays(5), null, null, seed.Owner).Value;
                seedDb.InventoryBatches.Add(lot); batches.Add(lot.Id);
            }
            var item = Item.Create(seed.Shop, "Different inherited policy", null, "pcs", Guid.NewGuid().ToString(), true, seed.Owner);
            var overriddenLot = InventoryBatch.Create(seed.Shop, item.Id, "override-lot", 10, 1, 2, 2, 0, false, Date.AddDays(-1), null, null, seed.Owner).Value;
            overriddenItem = item.Id; seedDb.AddRange(item, overriddenLot); batches.Add(overriddenLot.Id);
            await seedDb.SaveChangesAsync();
            var service = new ExpiryPolicyService(new ExpiryPolicyRepository(seedDb), new ShopRepository(seedDb), new ExpiryPolicyFence(seedDb), new UnitOfWork(seedDb), new BusinessClock(TimeProvider.System));
            Assert.False((await service.UpdateAsync(seed.Shop, seed.Owner, item.Id, new(2, new(ExpiryMode.Ignore, false, true, 7, false), "different item policy"), default)).IsError);
        }
        var counter = new PolicyReadCounter();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(counter).Options);
        var lines = new List<ValidatedSaleLine>();
        foreach (var batchId in batches) lines.Add(await Line(db, batchId));
        var guard = Guard(db);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.False((await new ExpiryPolicyFence(db).AcquireAsync(seed.Shop, false, default)).IsError);
            counter.Reset();
            var decisions = await guard.EvaluateAsync(seed.Shop, Date, lines, default);
            Assert.Equal(7, decisions.Count);
            Assert.Equal(4, decisions.Count(x => x.Decision.RequiresApproval));
            Assert.InRange(counter.Reads, 1, 4);
            var approvals = decisions.Where(x => x.Decision.RequiresApproval).Select(x => new ExpiryApprovalRequest(x.InventoryBatchId, x.Quantity, x.BatchVersion, x.Decision.PolicyVersion, x.Decision.CatalogVersion, "inspection")).ToArray();
            counter.Reset();
            var authorized = await guard.AuthorizeAsync(seed.Shop, seed.Owner, Date, lines, approvals, default);
            Assert.False(authorized.IsError);
            Assert.Equal(7, authorized.Value.Count);
            Assert.All(authorized.Value.Where(x => x.ItemId != overriddenItem), x => Assert.Equal(2, x.Policy.Version));
            Assert.Equal(3, authorized.Value.Single(x => x.ItemId == overriddenItem).Policy.Version);
            Assert.All(authorized.Value, x => Assert.Equal(3, x.CatalogVersion));
            Assert.InRange(counter.Reads, 1, 4);
            await tx.CommitAsync();
        }
        await using (var editor = Db())
        {
            var service = new ExpiryPolicyService(new ExpiryPolicyRepository(editor), new ShopRepository(editor), new ExpiryPolicyFence(editor), new UnitOfWork(editor), new BusinessClock(TimeProvider.System));
            Assert.False((await service.UpdateAsync(seed.Shop, seed.Owner, null, new(3, new(ExpiryMode.Block, false, true, 7, false), "next operation"), default)).IsError);
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.False((await new ExpiryPolicyFence(db).AcquireAsync(seed.Shop, false, default)).IsError);
            counter.Reset();
            var refreshed = await guard.EvaluateAsync(seed.Shop, Date, lines, default);
            Assert.Equal(4, refreshed.Count(x => !x.Decision.Allowed));
            Assert.All(refreshed, x => Assert.Equal(4, x.Decision.CatalogVersion));
            Assert.InRange(counter.Reads, 1, 4);
        }
    }
}
