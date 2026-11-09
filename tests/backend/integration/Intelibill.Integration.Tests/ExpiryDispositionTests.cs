using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpiryDispositionTests(PostgreSqlTestFixture fixture) : ExpiryDispositionFixture(fixture)
{
    [Fact]
    public async Task ExpiredCustomerReturn_IsQuarantinedNotSellable()
    {
        using var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop, quantity: 5m);
        await RecordReturnAsync(shop, sale, ReturnPayoutDestination.Refund, Line(sale.LineId, quantity: 2m));
        var stock = await QueryAsync(db => db.InventoryBatches.SingleAsync(x => x.Id == sale.BatchId));
        Assert.Equal(7m, stock.Quantity);
        Assert.Equal(2m, stock.QuarantinedQuantity);
        Assert.Equal(5m, stock.UnquarantinedQuantity);
        Assert.Equal(0, await QueryAsync(db => db.InventoryAdjustments.CountAsync(x => x.InventoryBatchId == sale.BatchId)));
    }

    [Fact]
    public async Task ReturnVoid_ReversesOriginalQuarantine()
    {
        using var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop, quantity: 5m);
        var id = await RecordReturnAsync(shop, sale, ReturnPayoutDestination.Refund, Line(sale.LineId, quantity: 2m));
        using var response = await SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{id}/void", new { reason = "Wrong return" });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var batch = await QueryAsync(db => db.InventoryBatches.SingleAsync(x => x.Id == sale.BatchId));
        Assert.Equal(5m, batch.Quantity); Assert.Equal(0m, batch.QuarantinedQuantity);
    }
    [Fact]
    public async Task Disposal_RecordsExactlyOneIndependentLoss_AndDurablyReplays()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m);
        var held = await RecoverAsync(shop, sale); var key = Guid.NewGuid().ToString();
        using var posted = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, key: key);
        var body = await posted.Content.ReadAsStringAsync(); Assert.True(posted.IsSuccessStatusCode, body);
        using (var policy = await SendAsync(shop, HttpMethod.Put, "/api/expiry-policies", new { expectedCatalogVersion = 1, policy = new { mode = 2, expiryRequired = false, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false }, reason = "Block expired selling after disposal" }))
            Assert.True(policy.IsSuccessStatusCode, await policy.Content.ReadAsStringAsync());
        using var replay = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, key: key);
        Assert.Equal(body, await replay.Content.ReadAsStringAsync());
        var batch = await BatchAsync(sale); Assert.Equal((5m, 0m), (batch.Quantity, batch.QuarantinedQuantity));
        var loss = Assert.Single(await QueryAsync(db => db.InventoryAdjustments.Where(x => x.InventoryBatchId == sale.BatchId).ToListAsync()));
        Assert.Equal((2m, 80m, 160m), (loss.Quantity, loss.UnitCost, loss.CostImpact)); Assert.NotNull(loss.DispositionChangeId);
        using var changed = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, 1m, key);
        await AssertCodeAsync(changed, "Operation.PayloadMismatch");
        using var voidLoss = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/adjustments/{loss.Id}/void", new { reason = "Undo disposal" });
        await AssertCodeAsync(voidLoss, "Expiry.DisposalCannotBeVoided");
        using var lookup = await SendAsync(shop, HttpMethod.Get, $"/api/financial-operations/batch-disposition/{key}");
        Assert.True(lookup.IsSuccessStatusCode, await lookup.Content.ReadAsStringAsync());
        Assert.Equal(2, await QueryAsync(db => db.BatchDispositionChanges.CountAsync(x => x.InventoryBatchId == sale.BatchId)));
    }

    [Fact]
    public async Task OriginallyExpiredRoot_CannotReleaseEvenAfterAuditedDateExtension()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        await CorrectDateAsync(shop, held.Batch); var batch = await BatchAsync(sale);
        using (var availability = await SendAsync(shop, HttpMethod.Get, $"/api/inventory/batches/available?barcode={batch.Item.Barcode}"))
        {
            var lot = (await availability.Content.ReadFromJsonAsync<JsonElement>())[0];
            Assert.Equal(7m,lot.GetProperty("quantity").GetDecimal()); Assert.Equal(2m,lot.GetProperty("quarantinedQuantity").GetDecimal()); Assert.Equal(5m,lot.GetProperty("availableQuantity").GetDecimal());
        }
        using var response = await DisposeAsync(shop, batch.Id, held.Root, batch.Version, action: "Release");
        await AssertCodeAsync(response, "Expiry.OriginallyExpired");
        Assert.Equal((7m, 2m), ((await BatchAsync(sale)).Quantity, (await BatchAsync(sale)).QuarantinedQuantity));
    }

    [Fact]
    public async Task MissingDateRoot_ReleaseRequiresAuditedCorrection_AndPreservesPhysicalStock()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m, missingDate: true);
        using (var policy = await SendAsync(shop, HttpMethod.Put, "/api/expiry-policies", new { expectedCatalogVersion = 1, policy = new { mode = 0, expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false }, reason = "Require source dates" }))
            Assert.True(policy.IsSuccessStatusCode, await policy.Content.ReadAsStringAsync());
        var held = await RecoverAsync(shop, sale);
        using (var refused = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, action: "Release")) await AssertCodeAsync(refused, "Expiry.ReleaseIneligible");
        await CorrectDateAsync(shop, held.Batch); var batch = await BatchAsync(sale);
        using var released = await DisposeAsync(shop, batch.Id, held.Root, batch.Version, action: "Release");
        Assert.True(released.IsSuccessStatusCode, await released.Content.ReadAsStringAsync());
        batch = await BatchAsync(sale); Assert.Equal((7m, 0m), (batch.Quantity, batch.QuarantinedQuantity));
        using var voided = await SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{held.Return}/void", new { reason = "Wrong return" });
        await AssertCodeAsync(voided, "Expiry.SourceDispositionConsumed");
    }

    [Fact]
    public async Task ReturnVoid_UsesOriginalRoot_AfterDateAndPolicyChange()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        await CorrectDateAsync(shop, held.Batch);
        using var voided = await SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{held.Return}/void", new { reason = "Wrong return" });
        Assert.True(voided.StatusCode == HttpStatusCode.NoContent, await voided.Content.ReadAsStringAsync());
        var batch = await BatchAsync(sale); Assert.Equal((5m,0m),(batch.Quantity,batch.QuarantinedQuantity));
        Assert.Equal(0, await QueryAsync(db => db.InventoryAdjustments.CountAsync(x => x.InventoryBatchId == batch.Id)));
        var changes = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == batch.Id).ToListAsync());
        var reverse = Assert.Single(changes, x => x.Action == "Reverse"); Assert.Equal(held.Root, reverse.RootChangeId);
    }

    [Fact]
    public async Task DisposedRoot_CannotVoidOrConsumeAnotherReturnsQuarantine()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m);
        var first = await RecoverAsync(shop, sale); var second = await RecoverAsync(shop, sale, 1m);
        using (var disposed = await DisposeAsync(shop, sale.BatchId, first.Root, second.Batch.Version, 1m)) Assert.True(disposed.IsSuccessStatusCode, await disposed.Content.ReadAsStringAsync());
        using (var refused = await SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{first.Return}/void", new { reason = "Wrong return" })) await AssertCodeAsync(refused, "Expiry.SourceDispositionConsumed");
        using (var allowed = await SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{second.Return}/void", new { reason = "Wrong return" })) Assert.True(allowed.StatusCode == HttpStatusCode.NoContent, await allowed.Content.ReadAsStringAsync());
        var batch = await BatchAsync(sale); Assert.Equal((6m, 1m), (batch.Quantity, batch.QuarantinedQuantity));
        var roots = await QueryAsync(db => db.BatchDispositionChanges.Where(x => x.InventoryBatchId == batch.Id).ToListAsync());
        Assert.Equal(batch.QuarantinedQuantity, roots.Sum(x => x.RootChangeId is null ? x.Quantity : -x.Quantity));
    }

    [Fact]
    public async Task OrdinaryCountAndDecrease_CannotConsumeQuarantinedSubset()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        using (var count = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{sale.BatchId}/count", new { expectedVersion = held.Batch.Version, countedQuantity = 1m, reason = "Physical count" })) await AssertCodeAsync(count, "Expiry.QuarantineProtected");
        using (var decrease = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{sale.BatchId}/adjust", new { direction = 2, reason = 6, quantity = 6m, notes = "Ordinary loss" })) Assert.False(decrease.IsSuccessStatusCode);
        var batch = await BatchAsync(sale); Assert.Equal((7m,2m),(batch.Quantity,batch.QuarantinedQuantity));
    }

    [Fact]
    public async Task Disposition_ValidatesRootScopeQuantityScaleAndVersion()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        using (var scale = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, .5m)) await AssertCodeAsync(scale, "Expiry.QuantityPrecisionExceeded");
        using (var stale = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version + 1)) await AssertCodeAsync(stale, "Expiry.BatchVersionConflict");
        using (var root = await DisposeAsync(shop, sale.BatchId, Guid.NewGuid(), held.Batch.Version)) await AssertCodeAsync(root, "Expiry.RootMismatch");
        using (var excessive = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, 3m)) await AssertCodeAsync(excessive, "Expiry.RootInsufficient");
        using var other = await OpenShopAsync(); var otherSale = await SellGoodsAsync(other, quantity: 5m); var otherBatch = await BatchAsync(otherSale);
        using (var cross = await DisposeAsync(other, otherSale.BatchId, held.Root, otherBatch.Version)) await AssertCodeAsync(cross, "Expiry.RootMismatch");
        using (var history = await SendAsync(other, HttpMethod.Get, $"/api/inventory/batches/{sale.BatchId}/dispositions")) Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
    }

    [Fact]
    public async Task MissingDateRoot_UnauditedCurrentDateCannotAuthorizeRelease()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m, missingDate: true);
        using (var policy = await SendAsync(shop, HttpMethod.Put, "/api/expiry-policies", new { expectedCatalogVersion = 1, policy = new { mode = 0, expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false }, reason = "Require source dates" }))
            Assert.True(policy.IsSuccessStatusCode, await policy.Content.ReadAsStringAsync());
        var held = await RecoverAsync(shop, sale);
        await QueryAsync(db => db.Database.ExecuteSqlAsync($"UPDATE inventory_batches SET expiry_date = {Today.AddDays(30)} WHERE id = {sale.BatchId}"));
        var batch = await BatchAsync(sale);
        using var response = await DisposeAsync(shop, batch.Id, held.Root, batch.Version, action: "Release");
        await AssertCodeAsync(response, "Expiry.ReleaseIneligible");
    }

    [Fact]
    public async Task RestrictedRole_CannotChangeRetainedRootOrSource_AndReadsOnlyOwnShop()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        using var other = await OpenShopAsync(); var foreign = await SellGoodsAsync(other, quantity: 5m); var foreignHeld = await RecoverAsync(other, foreign);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(RuntimeConnectionString).UseSnakeCaseNamingConvention().Options);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {held.Batch.ShopId.ToString()}, true)");
            Assert.Equal(0, await db.BatchDispositionChanges.CountAsync(x => x.Id == foreignHeld.Root));
            Assert.Single(await db.BatchDispositionChanges.ToListAsync());
            var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE batch_disposition_changes SET quantity = 1 WHERE id = {held.Root}"));
            Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, exception.SqlState);
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {held.Batch.ShopId.ToString()}, true)");
            var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM batch_disposition_changes WHERE id = {held.Root}"));
            Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, exception.SqlState);
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {held.Batch.ShopId.ToString()}, true)");
            var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync($"UPDATE sale_return_items SET quarantine_change_id = {foreignHeld.Root} WHERE sale_return_id = {held.Return}"));
            Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, exception.SqlState);
        }
    }

    [Fact]
    public async Task DeferredConstraints_RejectInventedRootLineageAndUnjournaledQuarantine()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(RuntimeConnectionString).UseSnakeCaseNamingConvention().Options);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {held.Batch.ShopId.ToString()}, true)");
            await db.Database.ExecuteSqlAsync($"UPDATE inventory_batches SET quarantined_quantity = 1 WHERE id = {sale.BatchId}");
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO batch_disposition_changes (id, shop_id, item_id, inventory_batch_id, root_change_id, action, quantity,
                    source_kind, source_id, source_line_id, expiry_date, manufacturing_date, reason_code, reason, policy_json, business_date, actor_user_id, recorded_at, created_at)
                SELECT {Guid.NewGuid()}, shop_id, item_id, inventory_batch_id, id, 'Release', 1, source_kind, {Guid.NewGuid()}, source_line_id,
                    expiry_date, manufacturing_date, reason_code, 'Incorrect source', policy_json, business_date, actor_user_id, recorded_at, created_at
                FROM batch_disposition_changes WHERE id = {held.Root}
                """);
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => tx.CommitAsync()); Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {held.Batch.ShopId.ToString()}, true)");
            await db.Database.ExecuteSqlAsync($"UPDATE inventory_batches SET quarantined_quantity = 0 WHERE id = {sale.BatchId}");
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => tx.CommitAsync()); Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        var batch = await BatchAsync(sale); Assert.Equal((7m,2m),(batch.Quantity,batch.QuarantinedQuantity));
    }

    [Fact]
    public async Task Disposition_ReplayRefusesLostOwnerAuthority()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale); var key = Guid.NewGuid().ToString();
        using (var posted = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, key: key)) Assert.True(posted.IsSuccessStatusCode, await posted.Content.ReadAsStringAsync());
        await QueryAsync(db => db.Database.ExecuteSqlAsync($"UPDATE shop_memberships SET role = 'Manager', authority_version = authority_version + 1 WHERE shop_id = {held.Batch.ShopId}"));
        using var replay = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, key: key); Assert.False(replay.IsSuccessStatusCode);
        await using var scope = Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<Intelibill.Application.Features.Expiry.BatchDispositionService>();
        var denied = await service.PostAsync(held.Batch.ShopId, held.Batch.CreatedBy, sale.BatchId, new(key, held.Batch.Version, held.Root, "Dispose", 2m, "Recovered goods disposal"), default);
        Assert.Equal("Expiry.OwnerRequired", denied.FirstError.Code);
    }

    [Fact]
    public async Task Disposition_ClosedPeriodRefusesNewMutationButRetainsPriorReplay()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale); var key = Guid.NewGuid().ToString();
        using (var posted = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, 1m, key)) Assert.True(posted.IsSuccessStatusCode, await posted.Content.ReadAsStringAsync());
        await QueryAsync(async db =>
        {
            var period = await db.PostingPeriods.SingleAsync(x => x.ShopId == held.Batch.ShopId);
            var closed = period.Close(held.Batch.CreatedBy, DateTimeOffset.UtcNow, period.EndDate.AddDays(1), "Closed fixture month");
            Assert.False(closed.IsError); db.Add(closed.Value); return await db.SaveChangesAsync();
        });
        using (var replay = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, 1m, key)) Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        var batch = await BatchAsync(sale);
        using var refused = await DisposeAsync(shop, sale.BatchId, held.Root, batch.Version, 1m); await AssertCodeAsync(refused, "PostingPeriod.Closed");
        Assert.Equal((6m,1m),((await BatchAsync(sale)).Quantity,(await BatchAsync(sale)).QuarantinedQuantity));
    }

    [Fact]
    public async Task PhysicalAndQuarantinedValuation_AreReportedSeparately()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); await RecoverAsync(shop, sale);
        using var response = await SendAsync(shop, HttpMethod.Get, "/api/inventory/batches");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var batch = Assert.Single(body.EnumerateArray(), x => x.GetProperty("id").GetGuid() == sale.BatchId);
        Assert.Equal(560m, batch.GetProperty("physicalRecordedValue").GetDecimal());
        Assert.Equal(160m, batch.GetProperty("quarantinedRecordedValue").GetDecimal());
        Assert.Equal(400m, batch.GetProperty("unquarantinedRecordedValue").GetDecimal());
        using var dashboard = await SendAsync(shop, HttpMethod.Get, "/api/dashboard");
        Assert.True(dashboard.IsSuccessStatusCode, await dashboard.Content.ReadAsStringAsync());
        var summary = await dashboard.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(560m,summary.GetProperty("stockValue").GetDecimal());
        Assert.Equal(2m,summary.GetProperty("quarantinedStockQuantity").GetDecimal());
        Assert.Equal(160m,summary.GetProperty("quarantinedStockValue").GetDecimal());
    }
    [Fact]
    public async Task ReturnAccountingAndExport_ReverseCogsOnce_AndRecognizeOnlyExplicitDisposal()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        using (var report = await SendAsync(shop, HttpMethod.Get, $"/api/sales/profit-loss?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))
        {
            var e = (await report.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("summary").GetProperty("economics");
            Assert.Equal(240m,e.GetProperty("cogs").GetDecimal()); Assert.Equal(0m,e.GetProperty("stockLoss").GetDecimal());
        }
        using (var posted = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version)) Assert.True(posted.IsSuccessStatusCode, await posted.Content.ReadAsStringAsync());
        using (var report = await SendAsync(shop, HttpMethod.Get, $"/api/sales/profit-loss?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))
        {
            var e = (await report.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("summary").GetProperty("economics");
            Assert.Equal(240m,e.GetProperty("cogs").GetDecimal()); Assert.Equal(160m,e.GetProperty("stockLoss").GetDecimal());
        }
        using var export = await SendAsync(shop, HttpMethod.Get, $"/api/exports/profit-loss?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}&format=xlsx");
        Assert.True(export.IsSuccessStatusCode, await export.Content.ReadAsStringAsync());
        using var stream = new MemoryStream(await export.Content.ReadAsByteArrayAsync()); using var book = new XLWorkbook(stream);
        var sheet = book.Worksheet("Profit & Loss");
        Assert.Equal(240m,sheet.RowsUsed().Single(x => x.Cell(1).GetString() == "COGS").Cell(2).GetValue<decimal>());
        Assert.Equal(160m,sheet.RowsUsed().Single(x => x.Cell(1).GetString() == "Stock loss").Cell(2).GetValue<decimal>());
    }
    [Fact]
    public async Task FractionalDisposition_ConservesQuantityAndRecordedCostAcrossPartialDisposals()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m, fractional: true); var held = await RecoverAsync(shop, sale, 2.125m);
        using (var first = await DisposeAsync(shop, sale.BatchId, held.Root, held.Batch.Version, .125m)) Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var batch = await BatchAsync(sale); Assert.Equal((7m,2m),(batch.Quantity,batch.QuarantinedQuantity));
        using (var rest = await DisposeAsync(shop, sale.BatchId, held.Root, batch.Version)) Assert.True(rest.IsSuccessStatusCode, await rest.Content.ReadAsStringAsync());
        batch = await BatchAsync(sale); Assert.Equal((5m,0m),(batch.Quantity,batch.QuarantinedQuantity));
        var losses = await QueryAsync(db => db.InventoryAdjustments.Where(x => x.InventoryBatchId == batch.Id).ToListAsync());
        Assert.Equal(2,losses.Count); Assert.Equal(2.125m,losses.Sum(x => x.Quantity)); Assert.Equal(170m,losses.Sum(x => x.CostImpact));
    }

    [Fact]
    public async Task OrdinaryCountAndAdjustmentVoids_PreserveOriginalQuarantinedSubset()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        using (var counted = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{sale.BatchId}/count", new { expectedVersion = held.Batch.Version, countedQuantity = 10m, reason = "Physical count includes held units" }))
            Assert.True(counted.IsSuccessStatusCode, await counted.Content.ReadAsStringAsync());
        foreach (var direction in new[] { 1, 2 })
        {
            using var adjustment = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{sale.BatchId}/adjust", new { direction, reason = direction == 1 ? 9 : 6, quantity = 1m, notes = "Ordinary stock correction" });
            Assert.True(adjustment.IsSuccessStatusCode, await adjustment.Content.ReadAsStringAsync());
            var id = (await adjustment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("adjustmentId").GetGuid();
            Assert.Equal(2m,(await BatchAsync(sale)).QuarantinedQuantity);
            using var reversed = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/adjustments/{id}/void", new { reason = "Correction entered incorrectly" });
            Assert.True(reversed.IsSuccessStatusCode, await reversed.Content.ReadAsStringAsync());
            var batch = await BatchAsync(sale); Assert.Equal((10m,2m),(batch.Quantity,batch.QuarantinedQuantity));
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceLineInsert_CannotClaimAnotherRoot(bool crossShop)
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m); var held = await RecoverAsync(shop, sale);
        Guid claimedRoot = held.Root;
        if (crossShop)
        {
            using var other = await OpenShopAsync(); var otherSale = await SellGoodsAsync(other, quantity: 5m); claimedRoot = (await RecoverAsync(other,otherSale)).Root;
        }
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(RuntimeConnectionString).UseSnakeCaseNamingConvention().Options);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.active_shop_id', {held.Batch.ShopId.ToString()}, true)");
        var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO sale_return_items (id,sale_return_id,shop_id,sale_id,sale_item_id,quantity,condition,original_cost_price,original_sales_price,
                    original_tax_rate_percent,original_is_price_including_tax,max_refund_amount,approved_refund_amount,taxable_amount,tax_amount,notes,
                    adjustment_taxable_amount,adjustment_tax_amount,adjustment_non_tax_amount,quarantine_change_id,quarantined_quantity,disposition_reason_code,created_at)
                SELECT {Guid.NewGuid()},sale_return_id,shop_id,sale_id,sale_item_id,quantity,condition,original_cost_price,original_sales_price,
                    original_tax_rate_percent,original_is_price_including_tax,max_refund_amount,approved_refund_amount,taxable_amount,tax_amount,notes,
                    adjustment_taxable_amount,adjustment_tax_amount,adjustment_non_tax_amount,{claimedRoot},quarantined_quantity,disposition_reason_code,created_at
                FROM sale_return_items WHERE sale_return_id = {held.Return}
                """);
            await tx.CommitAsync();
        });
        Assert.Equal(crossShop ? Npgsql.PostgresErrorCodes.ForeignKeyViolation : Npgsql.PostgresErrorCodes.CheckViolation,exception.SqlState);
    }
    [Fact]
    public void DispositionMigration_RefusesDestructiveDownBeforeAnyOperation()
    {
        var migration = new Intelibill.Infrastructure.Migrations.AddBatchDispositionEvidence();
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var down = migration.GetType().GetMethod("Down",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => down.Invoke(migration,[builder]));
        Assert.IsType<NotSupportedException>(error.InnerException); Assert.Empty(builder.Operations);
    }
    [Fact]
    public async Task MissingDateDisposal_DoesNotRelabelUnknownDatesAsExpired()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop, quantity: 5m, missingDate: true);
        using (var policy = await SendAsync(shop, HttpMethod.Put, "/api/expiry-policies", new { expectedCatalogVersion = 1, policy = new { mode = 0, expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false }, reason = "Require source dates" }))
            Assert.True(policy.IsSuccessStatusCode, await policy.Content.ReadAsStringAsync());
        var held = await RecoverAsync(shop,sale);
        using var disposed = await DisposeAsync(shop,sale.BatchId,held.Root,held.Batch.Version,1m);
        Assert.True(disposed.IsSuccessStatusCode,await disposed.Content.ReadAsStringAsync());
        var loss = await QueryAsync(db => db.InventoryAdjustments.SingleAsync(x => x.InventoryBatchId == sale.BatchId));
        Assert.Equal(InventoryAdjustmentReason.OtherLoss,loss.Reason);
        var root = await QueryAsync(db => db.BatchDispositionChanges.SingleAsync(x => x.Id == held.Root));
        Assert.Equal("Expiry.RequiredDateMissing",root.ReasonCode); Assert.Null(root.ExpiryDate);
        Assert.Equal(80m,loss.CostImpact);
    }
    [Fact]
    public async Task CleanReturnVoid_CannotTakeAnotherReturnsCurrentQuarantine()
    {
        using var shop = await OpenShopAsync(); var sale = await SellGoodsAsync(shop,quantity:5m);
        await CorrectDateAsync(shop,await BatchAsync(sale));
        var cleanReturn = await RecordReturnAsync(shop,sale,ReturnPayoutDestination.Refund,Line(sale.LineId,quantity:1m));
        Assert.Null((await ReturnAsync(cleanReturn)).Items.Single().QuarantineChangeId);
        var b = await BatchAsync(sale);
        using (var dated = await SendAsync(shop,HttpMethod.Put,$"/api/inventory/batches/{b.Id}/prices",new { expectedVersion = b.Version,batchNumber = b.BatchNumber,b.Mrp,b.SalesPrice,b.TaxRatePercent,b.TaxIncluded,
            expiryDate = Today.AddDays(-1),manufacturingDate = (DateOnly?)null,reason = "Corrected original expiry evidence" })) Assert.True(dated.IsSuccessStatusCode,await dated.Content.ReadAsStringAsync());
        var held = await RecoverAsync(shop,sale);
        using (var sold = await SellCurrentAsync(shop,held.Batch,6m)) Assert.True(sold.IsSuccessStatusCode,await sold.Content.ReadAsStringAsync());
        using var refused = await SendAsync(shop,HttpMethod.Post,$"/api/sales/returns/{cleanReturn}/void",new { reason = "Wrong clean return" });
        await AssertCodeAsync(refused,"InventoryBatch.InsufficientStock");
        Assert.False((await ReturnAsync(cleanReturn)).IsVoided);
        var current = await BatchAsync(sale); Assert.Equal((2m,2m),(current.Quantity,current.QuarantinedQuantity));
        Assert.Equal(1,await QueryAsync(db => db.BatchDispositionChanges.CountAsync(x => x.InventoryBatchId == current.Id)));
    }
}
