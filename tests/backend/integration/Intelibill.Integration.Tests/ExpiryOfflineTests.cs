using Intelibill.Application.Features.OfflineSalesSnapshot.Services;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Intelibill.Infrastructure.Services.Expiry;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpiryOfflineTests(PostgreSqlTestFixture fixture)
{
    private ApplicationDbContext Db(bool runtime = false) => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(runtime ? fixture.RuntimeConnectionString : fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);
    private static readonly DateTimeOffset Captured = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Issued = Captured.AddHours(1);
    private static readonly DateOnly IssueDate = new(2026, 10, 20);
    private static OfflineExpiryAuthority Authority(ApplicationDbContext db) => new(new OfflineExpiryEvidenceRepository(db), new ExpiryPolicyRepository(db), new ShopRepository(db), new BusinessClock(TimeProvider.System));
    private async Task<(Guid Shop, Guid Owner, Guid Batch, Guid Evidence)> Seed(ExpiryMode mode = ExpiryMode.Block, bool required = false)
    {
        await using var db = Db();
        var shop = Shop.Create("Offline expiry", "address", "city", "state", "560001", null, null, null);
        var owner = User.CreateWithEmail($"offline-expiry-{Guid.NewGuid():N}@example.test", "hash", "Owner", "Name");
        var item = Item.Create(shop.Id, "Expiry product", null, "pcs", Guid.NewGuid().ToString(), true, owner.Id);
        var batch = InventoryBatch.Create(shop.Id, item.Id, "lot", 10, 1, 2, 2, 0, false, IssueDate.AddDays(1), null, null, owner.Id).Value;
        db.AddRange(shop, owner, item, batch, ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, false));
        var policies = new ExpiryPolicyRepository(db);
        await policies.InitializeAsync(shop.Id, owner.Id, Captured.AddDays(-3), default);
        await db.SaveChangesAsync();
        var state = (await policies.GetStateAsync(shop.Id, default))!;
        state.Advance(true);
        var policy = new ExpiryPolicy(2, mode, required, true, 7, true);
        await policies.AddRevisionAsync(ExpiryPolicyRevision.Create(shop.Id, null, 2, policy, owner.Id, "initial policy", Captured.AddDays(-2)), default);
        await db.SaveChangesAsync();
        var evidence = OfflineExpiryEvidence.Capture(Guid.NewGuid(), batch, policy, 2, Captured);
        db.Add(evidence); await db.SaveChangesAsync();
        return (shop.Id, owner.Id, batch.Id, evidence.Id);
    }
    private static async Task<(OfflineSalePostingRequest Request, ValidatedSaleLine Line)> Request(ApplicationDbContext db, (Guid Shop, Guid Owner, Guid Batch, Guid Evidence) seed, DateTimeOffset? issued = null)
    {
        var batch = await db.InventoryBatches.Include(x => x.Item).SingleAsync(x => x.Id == seed.Batch);
        var line = new ValidatedSaleLine(new RecordSaleItemCommand(batch.Item.Barcode, batch.BatchNumber, batch.Item.Name, 1, 1, 2, 2, 0, false, batch.Id), batch.Item, batch, false);
        var frozen = new OfflineSaleSyncLineCommand(batch.Item.Barcode, batch.BatchNumber, batch.Item.Name, 1, 1, 2, 2, 0, false, batch.Id, 2, 0, 0, 2, 0, 2, null, null, InstantDiscountType.None, 0, null,
            ExpiryEvidence: new(seed.Evidence, 2, 2, IssueDate, IssueDate.AddDays(1), "Expiry.ExpiringSoon"));
        var sale = new OfflineSaleSyncCommand("client", "A/2627/000001", issued ?? Issued, null, null, null, PaymentMethod.Cash, 2, 0, 2, 2, 0, 0, 2, InstantDiscountType.None, 0, null, null, null, null, [frozen]);
        return (new(seed.Shop, "device", "client", sale, "hash", seed.Owner, seed.Owner, null, Captured.AddDays(4), new Dictionary<Guid, DiscountRule>()), line);
    }
    [Fact]
    public async Task OfflineIssueBeforeExpiry_SyncAfterExpiry_PreservesIssueDecision()
    {
        var seed = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        var result = await Authority(db).ValidateAsync(request.Request, [request.Line], default);
        Assert.False(result.IsError); var saved = Assert.Single(result.Value);
        Assert.Equal(IssueDate.AddDays(1), saved.ExpiryDate); Assert.Equal(2, saved.Policy.Version); Assert.Equal(IssueDate, saved.BusinessDate);
        Assert.Null(saved.ApprovedBy); Assert.Equal(1, saved.Quantity);
    }
    [Theory]
    [InlineData("missing")][InlineData("tenant")][InlineData("catalog")][InlineData("policy")][InlineData("date")][InlineData("businessdate")][InlineData("batch")][InlineData("result")][InlineData("before")][InlineData("expired")][InlineData("future")]
    public async Task OfflinePolicyUnknown_RequiresReview(string tamper)
    {
        var seed = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        var sale = request.Request.Sale; var line = sale.Items[0]; var proof = line.ExpiryEvidence!;
        proof = tamper switch { "missing" => proof with { EvidenceId = Guid.NewGuid() }, "catalog" => proof with { CatalogVersion = 1 }, "policy" => proof with { PolicyVersion = 1 }, "date" => proof with { ExpiryDate = IssueDate.AddDays(5) }, "businessdate" => proof with { BusinessDate = IssueDate.AddDays(1) }, "result" => proof with { DecisionCode = "Allowed" }, _ => proof };
        sale = sale with { Items = [line with { ExpiryEvidence = proof, InventoryBatchId = tamper == "batch" ? Guid.NewGuid() : seed.Batch }], SoldAt = tamper switch { "before" => Captured.AddSeconds(-1), "expired" => Captured.AddDays(8), "future" => request.Request.Now.AddDays(1), _ => sale.SoldAt } };
        var command = request.Request with { Sale = sale, ShopId = tamper == "tenant" ? Guid.NewGuid() : seed.Shop };
        var result = await Authority(db).ValidateAsync(command, [request.Line], default);
        Assert.True(result.IsError); Assert.Equal("Expiry.ReviewRequired", result.FirstError.Code);
        Assert.Empty(await db.Sales.Where(x => x.ShopId == seed.Shop).ToListAsync());
        Assert.Equal(10, (await db.InventoryBatches.SingleAsync(x => x.Id == seed.Batch)).Quantity);
    }
    [Theory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(false, true)]
    public async Task RelevantRevision_OnlyChangesBeforeIssueInvalidateEvidence(bool policyEdit, bool afterIssue)
    {
        var seed = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        var at = afterIssue ? Issued.AddMinutes(1) : Issued.AddMinutes(-1);
        var policies = new ExpiryPolicyRepository(db); var state = (await policies.GetStateAsync(seed.Shop, default))!;
        var version = state.Advance(policyEdit);
        if (policyEdit) await policies.AddRevisionAsync(ExpiryPolicyRevision.Create(seed.Shop, null, version, new(version, ExpiryMode.Ignore, false, true, 7, false), seed.Owner, "edit", at), default);
        else db.Add(BatchExpiryRevision.Create(request.Line.Batch!, IssueDate.AddDays(8), null, seed.Owner, "date edit", at, version));
        await db.SaveChangesAsync();
        var result = await Authority(db).ValidateAsync(request.Request, [request.Line], default);
        Assert.Equal(!afterIssue, result.IsError);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Evidence_RetainedFactsAreImmutableAndTenantScoped(bool delete)
    {
        var seed = await Seed();
        await using var db = Db(runtime: true); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {Guid.NewGuid().ToString()}, true)");
        Assert.Empty(await db.OfflineExpiryEvidence.ToListAsync());
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {seed.Shop.ToString()}, true)");
        Assert.Single(await db.OfflineExpiryEvidence.ToListAsync());
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => delete
            ? db.Database.ExecuteSqlAsync($"DELETE FROM offline_expiry_evidence WHERE id = {seed.Evidence}")
            : db.Database.ExecuteSqlAsync($"UPDATE offline_expiry_evidence SET catalog_version = 99 WHERE id = {seed.Evidence}"));
        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, error.SqlState);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task UnrelatedRevision_DoesNotInvalidateHistoricalEvidence(bool dateRevision)
    {
        var seed = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        var otherItem = Item.Create(seed.Shop, "Other", null, "pcs", Guid.NewGuid().ToString(), true, seed.Owner);
        var otherBatch = InventoryBatch.Create(seed.Shop, otherItem.Id, "other", 10, 1, 2, 2, 0, false, IssueDate.AddDays(8), null, null, seed.Owner).Value;
        db.AddRange(otherItem, otherBatch); await db.SaveChangesAsync();
        var policies = new ExpiryPolicyRepository(db); var state = (await policies.GetStateAsync(seed.Shop, default))!; var version = state.Advance();
        if (dateRevision) db.Add(BatchExpiryRevision.Create(otherBatch, IssueDate.AddDays(9), null, seed.Owner, "other lot label", Captured.AddMinutes(5), version));
        else await policies.AddRevisionAsync(ExpiryPolicyRevision.Create(seed.Shop, otherItem.Id, version, new(version, ExpiryMode.Ignore, false, true, 7, false), seed.Owner, "other item", Captured.AddMinutes(5)), default);
        await db.SaveChangesAsync();
        Assert.False((await Authority(db).ValidateAsync(request.Request, [request.Line], default)).IsError);
        if (dateRevision)
        {
            // The retained catalog can include a date-only version absent from historical POLICY maxima.
            var proof = OfflineExpiryEvidence.Capture(Guid.NewGuid(), request.Line.Batch!, new(2, ExpiryMode.Block, false, true, 7, true), 3, Captured.AddMinutes(10));
            db.Add(proof); await db.SaveChangesAsync();
            var line = request.Request.Sale.Items[0]; var next = line with { ExpiryEvidence = line.ExpiryEvidence! with { EvidenceId = proof.Id, CatalogVersion = 3 } };
            Assert.False((await Authority(db).ValidateAsync(request.Request with { Sale = request.Request.Sale with { Items = [next] } }, [request.Line], default)).IsError);
        }
    }
    [Fact]
    public async Task MissingCatalogRevision_CannotCertifyRetainedPolicy()
    {
        var seed = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        (await new ExpiryPolicyRepository(db).GetStateAsync(seed.Shop, default))!.Advance(); await db.SaveChangesAsync();
        Assert.True((await Authority(db).ValidateAsync(request.Request, [request.Line], default)).IsError);
    }
    [Fact]
    public async Task WarningApproval_CannotBeForgedWhileDisconnected()
    {
        var seed = await Seed(ExpiryMode.Warn); await using var db = Db(); var request = await Request(db, seed, Captured.AddDays(2));
        var line = request.Request.Sale.Items[0]; var claim = line with { ExpiryEvidence = line.ExpiryEvidence! with { BusinessDate = IssueDate.AddDays(2), DecisionCode = "Expiry.Expired" } };
        var result = await Authority(db).ValidateAsync(request.Request with { Sale = request.Request.Sale with { Items = [claim] } }, [request.Line], default);
        Assert.True(result.IsError);
    }
    [Fact]
    public async Task LegacyIgnore_OnlyMigrationBaselineBeforeActivationIsSupported()
    {
        var seed = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE expiry_policy_revisions DISABLE TRIGGER expiry_policy_revisions_immutable");
        await db.Database.ExecuteSqlAsync($"UPDATE expiry_policy_revisions SET pre_feature_baseline = true, actor_user_id = NULL WHERE shop_id = {seed.Shop} AND catalog_version = 1");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE expiry_policy_revisions ENABLE TRIGGER expiry_policy_revisions_immutable");
        await db.Database.ExecuteSqlAsync($"UPDATE shop_expiry_states SET pre_feature_baseline = true WHERE shop_id = {seed.Shop}");
        var legacy = request.Request.Sale with { SoldAt = Captured.AddDays(-2).AddMinutes(-1), Items = [request.Request.Sale.Items[0] with { ExpiryEvidence = null }] };
        Assert.False((await Authority(db).ValidateAsync(request.Request with { Sale = legacy }, [request.Line], default)).IsError);
        var policies = new ExpiryPolicyRepository(db); var state = (await policies.GetStateAsync(seed.Shop, default))!; var version = state.Advance(true);
        await policies.AddRevisionAsync(ExpiryPolicyRevision.Create(seed.Shop, null, version, new(version, ExpiryMode.Ignore, false, true, 7, false), seed.Owner, "back to ignore", Captured.AddMinutes(1)), default);
        await db.SaveChangesAsync();
        Assert.True((await Authority(db).ValidateAsync(request.Request with { Sale = legacy with { SoldAt = Issued } }, [request.Line], default)).IsError);
    }

    private static OfflineSalesSnapshotStreamingService Snapshot(ApplicationDbContext db) => new(new UserRepository(db), new CustomerRepository(db), new DiscountRuleRepository(db),
        new InvoiceLeaseRepository(db), new ServiceRepository(db), new OfflineExpiryEvidenceRepository(db), new ExpiryPolicyRepository(db), new ExpiryPolicyFence(db), new UnitOfWork(db), new ShopRepository(db), new BusinessClock(TimeProvider.System));
    [Fact]
    public async Task Snapshot_BoundedPagesPersistProofBeforePublishingAndOnlyCompleteAfterAllRows()
    {
        var seed = await Seed();
        await using (var db = Db())
        {
            var source = await db.InventoryBatches.SingleAsync(x => x.Id == seed.Batch);
            for (var i = 0; i < 104; i++) db.Add(InventoryBatch.Create(seed.Shop, source.ItemId, $"snapshot-{i}", 10, 1, 2, 2, 0, false, IssueDate.AddDays(1), null, null, seed.Owner).Value);
            await db.SaveChangesAsync();
        }
        await using var capture = Db(); var snapshotId = Guid.NewGuid(); var count = 0; var complete = false;
        await foreach (var record in Snapshot(capture).StreamAsync(seed.Shop, snapshotId, DateTimeOffset.UtcNow, default))
        {
            if (record is OfflineSalesSnapshotBatchRecord batch)
            {
                Assert.False(complete);
                await using var observer = Db();
                Assert.True(await observer.OfflineExpiryEvidence.AnyAsync(x => x.Id == batch.Batch.ExpiryEvidenceId && x.SnapshotId == snapshotId));
                count++;
            }
            if (record is OfflineSalesSnapshotCompleteRecord done) { complete = true; Assert.Equal(105, done.Complete.BatchCount); }
        }
        Assert.True(complete); Assert.Equal(105, count);
    }
    [Fact]
    public async Task Snapshot_TruncationOrCatalogEditNeverEmitsCompletion()
    {
        var seed = await Seed(); await using var capture = Db(); var records = Snapshot(capture).StreamAsync(seed.Shop, Guid.NewGuid(), DateTimeOffset.UtcNow, default).GetAsyncEnumerator();
        await using (records)
        {
            Assert.True(await records.MoveNextAsync()); Assert.IsType<OfflineSalesSnapshotMetadataRecord>(records.Current);
            Assert.True(await records.MoveNextAsync()); Assert.IsType<OfflineSalesSnapshotBatchRecord>(records.Current);
            await using var edit = Db(); var policies = new ExpiryPolicyRepository(edit); var state = (await policies.GetStateAsync(seed.Shop, default))!;
            var version = state.Advance(true); await policies.AddRevisionAsync(ExpiryPolicyRevision.Create(seed.Shop, null, version, new(version, ExpiryMode.Ignore, false, true, 7, false), seed.Owner, "catalog changed during stream", DateTimeOffset.UtcNow), default); await edit.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { while (await records.MoveNextAsync()) Assert.IsNotType<OfflineSalesSnapshotCompleteRecord>(records.Current); });
        }
        // A consumer which stops at a lot sees persisted evidence but never a completion marker.
        await using var interrupted = Snapshot(capture).StreamAsync(seed.Shop, Guid.NewGuid(), DateTimeOffset.UtcNow, default).GetAsyncEnumerator();
        Assert.True(await interrupted.MoveNextAsync()); Assert.IsType<OfflineSalesSnapshotMetadataRecord>(interrupted.Current);
        Assert.True(await interrupted.MoveNextAsync()); Assert.IsType<OfflineSalesSnapshotBatchRecord>(interrupted.Current);
    }
    [Fact]
    public async Task PartialNewEvidence_CannotMixWithLegacyIgnoreFallback()
    {
        var seed = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE expiry_policy_revisions DISABLE TRIGGER expiry_policy_revisions_immutable");
        await db.Database.ExecuteSqlAsync($"DELETE FROM expiry_policy_revisions WHERE shop_id = {seed.Shop} AND catalog_version = 2");
        await db.Database.ExecuteSqlAsync($"UPDATE expiry_policy_revisions SET pre_feature_baseline = true, actor_user_id = NULL WHERE shop_id = {seed.Shop} AND catalog_version = 1");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE expiry_policy_revisions ENABLE TRIGGER expiry_policy_revisions_immutable");
        await db.Database.ExecuteSqlAsync($"UPDATE shop_expiry_states SET catalog_version = 1, default_policy_version = 1, pre_feature_baseline = true WHERE shop_id = {seed.Shop}");
        var proof = OfflineExpiryEvidence.Capture(Guid.NewGuid(), request.Line.Batch!, ExpiryPolicy.Default, 1, Captured);
        db.Add(proof); await db.SaveChangesAsync();
        var line = request.Request.Sale.Items[0]; var complete = line with { ExpiryEvidence = new(proof.Id, 1, 1, IssueDate, IssueDate.AddDays(1), "Allowed") };
        var result = await Authority(db).ValidateAsync(request.Request with { Sale = request.Request.Sale with { Items = [complete, line with { ExpiryEvidence = null }] } }, [request.Line, request.Line], default);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task DefaultEdit_UnrelatedToRetainedItemOverrideDoesNotInvalidateIssue()
    {
        var seed = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        var policies = new ExpiryPolicyRepository(db); var state = (await policies.GetStateAsync(seed.Shop, default))!;
        var version = state.Advance(); var overridePolicy = new ExpiryPolicy(version, ExpiryMode.Block, false, true, 7, false);
        await policies.AddRevisionAsync(ExpiryPolicyRevision.Create(seed.Shop, request.Line.Item!.Id, version, overridePolicy, seed.Owner, "item override", Captured.AddMinutes(-1)), default);
        var proof = OfflineExpiryEvidence.Capture(Guid.NewGuid(), request.Line.Batch!, overridePolicy, version, Captured);
        db.Add(proof); await db.SaveChangesAsync();
        var next = state.Advance(true); await policies.AddRevisionAsync(ExpiryPolicyRevision.Create(seed.Shop, null, next, new(next, ExpiryMode.Ignore, false, true, 7, false), seed.Owner, "different default", Captured.AddMinutes(5)), default);
        await db.SaveChangesAsync();
        var frozen = request.Request.Sale.Items[0] with { ExpiryEvidence = new(proof.Id, version, version, IssueDate, IssueDate.AddDays(1), "Expiry.ExpiringSoon") };
        Assert.False((await Authority(db).ValidateAsync(request.Request with { Sale = request.Request.Sale with { Items = [frozen] } }, [request.Line], default)).IsError);
    }

    [Fact]
    public async Task PostedSaleLink_RequiresExactEnvelopeHashAndRetainedDecisions()
    {
        var seed = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        var payload = request.Request.Sale; var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(seed.Shop, "device", payload);
        var sale = Sale.Create(seed.Shop, seed.Owner, "key", hash, payload.InvoiceNumber, null, null, null, PaymentMethod.Cash, Issued,
            new(IssueDate, "Asia/Kolkata"), 2, 0, 2, 0, [SaleItem.CreateGoods(seed.Shop, request.Line.Item!.Id, seed.Batch, request.Line.Item.Name, request.Line.Item.Barcode, 1, 1, 2, 2, 0, false, false)], source: SaleSource.Offline, clientSaleId: "client", deviceId: "device", syncedAt: request.Request.Now);
        var original = OfflineSubmission.Record(seed.Shop, "device", "client", payload.InvoiceNumber, hash, OfflineSubmissionPayload.Serialize(payload), Issued, request.Request.Now, OfflineSubmissionSource.Sync, seed.Owner, seed.Owner, seed.Owner, null, null);
        Assert.False(await Authority(db).VerifiesPostedSaleAsync(original, sale, default));
        var decisions = await Authority(db).ValidateAsync(request.Request, [request.Line], default); Assert.False(decisions.IsError);
        sale.AttachExpiryDecisions(decisions.Value);
        Assert.True(await Authority(db).VerifiesPostedSaleAsync(original, sale, default));
        var tampered = OfflineSubmission.Record(seed.Shop, "device", "client", payload.InvoiceNumber, hash, OfflineSubmissionPayload.Serialize(payload with { TotalAmount = 999m, PaidAmount = 999m }), Issued, request.Request.Now, OfflineSubmissionSource.Sync, seed.Owner, seed.Owner, seed.Owner, null, null);
        Assert.False(await Authority(db).VerifiesPostedSaleAsync(tampered, sale, default));
    }
    [Fact]
    public async Task Evidence_CompositeTenantForeignKeyRejectsCrossShopBatch()
    {
        var seed = await Seed(); var other = await Seed(); await using var db = Db(); var request = await Request(db, seed);
        var proof = OfflineExpiryEvidence.Capture(Guid.NewGuid(), request.Line.Batch!, new(2, ExpiryMode.Block, false, true, 7, true), 2, Captured);
        typeof(OfflineExpiryEvidence).GetProperty(nameof(OfflineExpiryEvidence.ShopId))!.SetValue(proof, other.Shop);
        db.Add(proof); var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(Npgsql.PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
    }
    [Fact]
    public void EvidenceMigration_RefusesDestructiveDownBeforeEmittingOperations()
    {
        var migration = new Intelibill.Infrastructure.Migrations.AddOfflineExpiryEvidence();
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var method = migration.GetType().GetMethod("Down", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(migration, [builder]));
        Assert.IsType<NotSupportedException>(error.InnerException); Assert.Empty(builder.Operations);
    }

}
