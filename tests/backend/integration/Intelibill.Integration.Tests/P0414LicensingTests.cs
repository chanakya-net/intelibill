using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Npgsql;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0414LicensingTests : ExpiryDispositionFixture
{
    private readonly TestClock _clock = new(EntitlementSnapshotsV1.Instant(TimeProvider.System));
    protected override ApiWebApplicationFactory Factory { get; }

    public P0414LicensingTests(PostgreSqlTestFixture fixture) : base(fixture)
    {
        Factory = new(fixture, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_clock);
        });
    }

    [Fact]
    public async Task Eligible_release_stops_at_paid_deadline_and_owner_can_dispose()
    {
        using var shop = await OpenShopAsync();
        var end = _clock.Now.AddMinutes(2);
        await GrantAsync(shop, end);
        var (sale, root, version) = await CreateReleasableHoldAsync(shop);

        _clock.Now = end.AddSeconds(-1);
        using (var prior = await DisposeAsync(shop, sale.BatchId, root, version, 1m, action: "Release"))
            Assert.True(prior.IsSuccessStatusCode, await prior.Content.ReadAsStringAsync());
        var batch = await BatchAsync(sale);
        Assert.Equal((7m, 1m), (batch.Quantity, batch.QuarantinedQuantity));

        _clock.Now = end;
        using (var denied = await DisposeAsync(shop, sale.BatchId, root, batch.Version, 1m, action: "Release"))
            await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "Licence.Expired");

        using var disposed = await DisposeAsync(shop, sale.BatchId, root, batch.Version, 1m);
        Assert.True(disposed.IsSuccessStatusCode, await disposed.Content.ReadAsStringAsync());
        batch = await BatchAsync(sale);
        Assert.Equal((6m, 0m, 6m), (batch.Quantity, batch.QuarantinedQuantity, batch.UnquarantinedQuantity));
        var loss = Assert.Single(await QueryAsync(db => db.InventoryAdjustments.Where(x => x.InventoryBatchId == sale.BatchId).ToListAsync()));
        Assert.Equal((1m, 80m), (loss.Quantity, loss.CostImpact));
        var change = await QueryAsync(db => db.BatchDispositionChanges.SingleAsync(x => x.Id == loss.DispositionChangeId));
        Assert.Equal(root, change.RootChangeId);
        var movement = await QueryAsync(db => db.StockTransactions.SingleAsync(x => x.Id == change.StockMovementId));
        Assert.Equal((StockTransactionType.Dmg, -1m), (movement.TransactionType, movement.Quantity));
    }

    [Fact]
    public async Task Restricted_disposal_requires_eligible_source_and_conserves_stock()
    {
        using var shop = await OpenShopAsync();
        using var other = await OpenShopAsync();
        var end = _clock.Now.AddMinutes(2);
        await GrantAsync(shop, end);
        var second = await SellGoodsAsync(shop, quantity: 5m, missingDate: true);
        var (sale, root, version) = await CreateReleasableHoldAsync(shop);
        var original = await BatchAsync(sale);
        var different = await BatchAsync(second);
        _clock.Now = end;
        var before = await FactsAsync(shop, original.ItemId);

        await RefuseDispositionAsync(shop, Guid.NewGuid(), root, version, "Inventory.BatchNotFound");
        await RefuseDispositionAsync(other, sale.BatchId, root, version, "Inventory.BatchNotFound");
        await RefuseDispositionAsync(shop, different.Id, root, different.Version, "Expiry.RootMismatch");
        await RefuseDispositionAsync(shop, sale.BatchId, root, version, "Expiry.QuantityPrecisionExceeded", 0.5m);
        await RefuseDispositionAsync(shop, sale.BatchId, root, version, "Expiry.RootInsufficient", 3m);
        await RefuseDispositionAsync(shop, sale.BatchId, root, version, "Expiry.DispositionInvalid", action: "Intake");

        using var count = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{sale.BatchId}/count",
            new { expectedVersion = version, countedQuantity = 8m, reason = "Safety correction", operationKey = NewKey() });
        await AssertProblemAsync(count, HttpStatusCode.Forbidden, "Licence.Expired");
        using var increase = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{sale.BatchId}/adjust",
            new { direction = "Increase", reason = "ReturnRestockCorrection", quantity = 1, notes = "Safety correction" });
        await AssertProblemAsync(increase, HttpStatusCode.Forbidden, "Licence.Expired");
        Assert.Equal(before, await FactsAsync(shop, original.ItemId));

        using var disposed = await DisposeAsync(shop, sale.BatchId, root, version);
        Assert.True(disposed.IsSuccessStatusCode, await disposed.Content.ReadAsStringAsync());
        var after = await BatchAsync(sale);
        Assert.Equal((5m, 0m, 5m), (after.Quantity, after.QuarantinedQuantity, after.UnquarantinedQuantity));
        var saved = await FactsAsync(shop, original.ItemId);
        await RefuseDispositionAsync(shop, sale.BatchId, root, after.Version, "Expiry.RootInsufficient");
        Assert.Equal(saved, await FactsAsync(shop, original.ItemId));
    }

    [Theory]
    [InlineData("PendingActivation", true, "Licence.PendingActivation")]
    [InlineData("Suspended", true, "Licence.TradingRestricted")]
    [InlineData("Expired", false, null)]
    public async Task Projection_respects_completion_and_observe_only(string state, bool enforced, string? refusal)
    {
        using var shop = await OpenShopAsync();
        var (sale, root, version) = await CreateReleasableHoldAsync(shop);
        await GrantAsync(shop, _clock.Now.AddMinutes(2), state: state, enforced: enforced);
        using var release = await DisposeAsync(shop, sale.BatchId, root, version, 1m, action: "Release");
        if (refusal is not null)
            await AssertProblemAsync(release, HttpStatusCode.Forbidden, refusal);
        else
            Assert.True(release.IsSuccessStatusCode, await release.Content.ReadAsStringAsync());

        var batch = await BatchAsync(sale);
        using var disposal = await DisposeAsync(shop, sale.BatchId, root, batch.Version, 1m);
        Assert.True(disposal.IsSuccessStatusCode, await disposal.Content.ReadAsStringAsync());
        Assert.Equal(6m, (await BatchAsync(sale)).Quantity);
    }

    [Fact]
    public async Task Restricted_completion_requires_a_hold_recorded_at_or_before_posting_time()
    {
        using var shop = await OpenShopAsync();
        var (sale, root, version) = await CreateReleasableHoldAsync(shop);
        var recordedAt = _clock.Now;
        await GrantAsync(shop, recordedAt.AddMinutes(2), verified: recordedAt.AddMinutes(-1), state: "Expired");
        _clock.Now = recordedAt.AddMilliseconds(-1);
        using var denied = await DisposeAsync(shop, sale.BatchId, root, version, 1m);
        await AssertProblemAsync(denied, HttpStatusCode.Conflict, "Expiry.RootIneligible");
        _clock.Now = recordedAt;
        using var disposed = await DisposeAsync(shop, sale.BatchId, root, version, 1m);
        Assert.True(disposed.IsSuccessStatusCode, await disposed.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposition_after_a_lock_wait_uses_current_authority_and_one_source_balance(bool outage)
    {
        using var shop = await OpenShopAsync();
        var cutoff = _clock.Now.AddMinutes(2);
        await GrantAsync(shop, outage ? cutoff.AddDays(1) : cutoff,
            outage ? cutoff - EntitlementSnapshotsV1.Freshness : null);
        var (sale, root, version) = await CreateReleasableHoldAsync(shop);
        var batch = await BatchAsync(sale);
        var before = await FactsAsync(shop, batch.ItemId);
        var release = await PostAcrossStockWaitAsync(shop, sale, root, version, cutoff, "Release");
        using (release)
            await AssertProblemAsync(release, outage ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden,
                outage ? "Licence.VerificationUnavailable" : "Licence.Expired");
        Assert.Equal(before, await FactsAsync(shop, batch.ItemId));

        var competing = await Task.WhenAll(
            DisposeAsync(shop, sale.BatchId, root, version),
            DisposeAsync(shop, sale.BatchId, root, version));
        using var winner = Assert.Single(competing, x => x.IsSuccessStatusCode);
        using var loser = Assert.Single(competing, x => !x.IsSuccessStatusCode);
        await AssertProblemAsync(loser, HttpStatusCode.Conflict, "Expiry.BatchVersionConflict");
        var after = await BatchAsync(sale);
        Assert.Equal((5m, 0m), (after.Quantity, after.QuarantinedQuantity));
        var saved = await FactsAsync(shop, batch.ItemId);
        Assert.Equal((before.Changes + 1, before.Receipts + 1, before.Movements + 1, before.Losses + 1, 5m), saved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_a_live_owner_can_recover_a_committed_disposition(bool accountSecurity)
    {
        using var shop = await OpenShopAsync();
        using var other = await OpenShopAsync();
        using var manager = await AddManagerAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        var security = end.AddMinutes(1);
        await GrantAsync(shop, end, security: new("Security", security, "Fixture suspension"), accountSecurity: accountSecurity);
        var (sale, root, version) = await CreateReleasableHoldAsync(shop);
        var key = NewKey();
        using var posted = await DisposeAsync(shop, sale.BatchId, root, version, 1m, key, "Release");
        Assert.True(posted.IsSuccessStatusCode, await posted.Content.ReadAsStringAsync());
        var committed = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changeId").GetGuid();
        var batch = await BatchAsync(sale);
        var before = await FactsAsync(shop, batch.ItemId);

        _clock.Now = end;
        using var replay = await DisposeAsync(shop, sale.BatchId, root, version, 1m, key, "Release");
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal(committed, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changeId").GetGuid());
        using var lookup = await LookupAsync(shop, key);
        Assert.True(lookup.IsSuccessStatusCode, await lookup.Content.ReadAsStringAsync());
        Assert.Equal(committed, (await lookup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("response").GetProperty("changeId").GetGuid());
        using var mismatch = await DisposeAsync(shop, sale.BatchId, root, version, 2m, key, "Release");
        await AssertProblemAsync(mismatch, HttpStatusCode.Conflict, "Operation.PayloadMismatch");
        using var fresh = await DisposeAsync(shop, sale.BatchId, root, batch.Version, 1m, action: "Release");
        await AssertProblemAsync(fresh, HttpStatusCode.Forbidden, "Licence.Expired");
        using var unused = await LookupAsync(shop, NewKey());
        using var isolated = await LookupAsync(other, key);
        using var unauthorized = await LookupAsync(manager, key);
        Assert.Equal(HttpStatusCode.NotFound, unused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, isolated.StatusCode);
        await AssertProblemAsync(unauthorized, HttpStatusCode.Forbidden, "Expiry.OwnerRequired");
        using var managerReplay = await DisposeAsync(manager, sale.BatchId, root, version, 1m, key, "Release");
        await AssertProblemAsync(managerReplay, HttpStatusCode.Forbidden, "Expiry.OwnerRequired");

        _clock.Now = security;
        using var securityReplay = await DisposeAsync(shop, sale.BatchId, root, version, 1m, key, "Release");
        using var securityLookup = await LookupAsync(shop, key);
        await AssertProblemAsync(securityReplay, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(securityLookup, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.Equal(before, await FactsAsync(shop, batch.ItemId));
    }

    [Fact]
    public async Task Disposal_rechecks_live_session_after_stock_lock_wait()
    {
        using var shop = await OpenShopAsync();
        var (sale, root, version) = await CreateReleasableHoldAsync(shop);
        var batch = await BatchAsync(sale);
        var before = await FactsAsync(shop, batch.ItemId);
        var sessionId = Guid.Parse(new JwtSecurityToken(shop.Token).Claims.Single(x => x.Type == "sid").Value);
        await using var db = Open(Factory.MigratorConnectionString);
        await using var blocker = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT id FROM inventory_batches WHERE id = {sale.BatchId} FOR NO KEY UPDATE");
        var key = NewKey();
        var posting = DisposeAsync(shop, sale.BatchId, root, version, key: key);
        try
        {
            await WaitForBlockedRequestAsync(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID, posting);
            await using var authority = Open(Factory.MigratorConnectionString);
            var session = await authority.AuthSessions.SingleAsync(x => x.Id == sessionId);
            session.Revoke("Fixture revoked device", _clock.Now);
            await authority.SaveChangesAsync();
        }
        finally
        {
            await blocker.RollbackAsync();
        }
        using var revoked = await posting;
        await AssertProblemAsync(revoked, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated");
        using var revokedLookup = await LookupAsync(shop, key);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedLookup.StatusCode);
        Assert.Equal(before, await FactsAsync(shop, batch.ItemId));
    }

    private async Task<Shop> AddManagerAsync(Shop shop)
    {
        var email = $"p0414-{Guid.NewGuid():N}@fixture.test";
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var enrollment = await client.EnrollAsync(new { email, password = "Pass123!Aa", firstName = "Test", lastName = "Manager" });
        enrollment.EnsureSuccessStatusCode();
        var member = new Shop(client, (await enrollment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!);
        var shopId = ShopId(shop);
        using var invited = await SendAsync(shop, HttpMethod.Post, $"/api/shops/{shopId}/invitations", new { email, role = "Manager" });
        Assert.True(invited.IsSuccessStatusCode, await invited.Content.ReadAsStringAsync());
        var invitation = await IdentityTestMailbox.ReadInvitationAsync(Factory, email);
        using var accepted = await SendAsync(member, HttpMethod.Post, $"/api/invitations/{invitation.Id}/accept", new { token = invitation.Token });
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
        using var scoped = await SendAsync(member, HttpMethod.Post, "/api/shops/switch", new { shopId });
        Assert.True(scoped.IsSuccessStatusCode, await scoped.Content.ReadAsStringAsync());
        return member with { Token = (await scoped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()! };
    }

    private static Task<HttpResponseMessage> LookupAsync(Shop shop, string key) =>
        SendAsync(shop, HttpMethod.Get, $"/api/financial-operations/batch-disposition/{key}");

    [Fact]
    public async Task Scheduled_security_suspension_blocks_disposal_after_stock_lock_wait()
    {
        using var shop = await OpenShopAsync();
        var security = _clock.Now.AddMinutes(1);
        await GrantAsync(shop, security.AddMinutes(1), security: new("Security", security, "Fixture suspension"));
        var (sale, root, version) = await CreateReleasableHoldAsync(shop);
        var batch = await BatchAsync(sale);
        var before = await FactsAsync(shop, batch.ItemId);
        using var disposal = await PostAcrossStockWaitAsync(shop, sale, root, version, security, "Dispose");
        await AssertProblemAsync(disposal, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.Equal(before, await FactsAsync(shop, batch.ItemId));
    }

    private async Task<HttpResponseMessage> PostAcrossStockWaitAsync(
        Shop shop, SoldSale sale, Guid root, uint version, DateTimeOffset cutoff, string action)
    {
        await using var db = Open(Factory.MigratorConnectionString);
        await using var blocker = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT id FROM inventory_batches WHERE id = {sale.BatchId} FOR NO KEY UPDATE");
        var pid = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
        var posting = DisposeAsync(shop, sale.BatchId, root, version, 1m, action: action);
        try
        {
            await WaitForBlockedRequestAsync(pid, posting);
            _clock.Now = cutoff;
        }
        finally
        {
            await blocker.RollbackAsync();
        }
        return await posting;
    }

    private async Task WaitForBlockedRequestAsync(int pid, Task<HttpResponseMessage> request)
    {
        await using var observer = Open(Factory.MigratorConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!request.IsCompleted)
        {
            var blocked = await observer.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
                    AND wait_event_type = 'Lock' AND {pid} = ANY(pg_blocking_pids(pg_stat_activity.pid))) AS "Value"
                """).SingleAsync(timeout.Token);
            if (blocked) return;
            await Task.Delay(10, timeout.Token);
        }

        using var reply = await request;
        Assert.Fail($"Request completed without waiting: {(int)reply.StatusCode} {await reply.Content.ReadAsStringAsync()}");
    }

    private async Task<(int Changes, int Receipts, int Movements, int Losses, decimal Total)> FactsAsync(Shop shop, Guid item)
    {
        var shopId = ShopId(shop);
        return await QueryAsync(async db => (
            await db.BatchDispositionChanges.CountAsync(x => x.ShopId == shopId && x.ItemId == item),
            await db.OperationReceipts.CountAsync(x => x.ShopId == shopId && x.OperationType == FinancialOperationType.BatchDisposition),
            await db.StockTransactions.CountAsync(x => x.ShopId == shopId && x.ItemId == item),
            await db.InventoryAdjustments.CountAsync(x => x.ShopId == shopId && x.ItemId == item),
            await db.Inventory.Where(x => x.ShopId == shopId && x.ItemId == item).Select(x => x.Quantity).SingleAsync()));
    }

    private static async Task RefuseDispositionAsync(Shop shop, Guid batchId, Guid root, uint version, string code,
        decimal quantity = 1m, string action = "Dispose")
    {
        using var denied = await DisposeAsync(shop, batchId, root, version, quantity, action: action);
        await AssertCodeAsync(denied, code);
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status} {code}, received {(int)response.StatusCode}: {content}");
        using var body = JsonDocument.Parse(content);
        Assert.Equal(code, (body.RootElement.TryGetProperty("code", out var stableCode)
            ? stableCode : body.RootElement.GetProperty("title")).GetString());
    }
}
