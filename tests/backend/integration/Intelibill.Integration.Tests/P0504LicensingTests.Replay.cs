using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Integration.Tests;

public sealed partial class P0504LicensingTests
{
    [Fact]
    public async Task A_proven_invoice_waits_for_reopening_and_replays_the_original_hash()
    {
        var start = At("2027-01-31T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var sale = await IssuedInvoiceAsync(client, actor, start, end, "valid");
        _clock.Now = start.AddDays(1);
        using var close = await client.PostAsJsonAsync("/api/posting-periods/2027-01/close", new { operationKey = "close", reason = "Close January" }, Ct);
        await ReadSuccessAsync(close);
        var held = await SyncAsync(client, sale);
        Assert.Equal("PostingPeriod.Closed", held.GetProperty("errors")[0].GetProperty("code").GetString());
        using var reopen = await client.PostAsJsonAsync("/api/posting-periods/2027-01/reopen", new { operationKey = "reopen", reason = "Late receipt" }, Ct);
        await ReadSuccessAsync(reopen);
        var path = $"/api/offline-reconciliation/{held.GetProperty("reviewCaseId").GetGuid():D}";
        var detail = await client.GetFromJsonAsync<JsonElement>(path, Ct);
        using var resolve = await client.PostAsJsonAsync(path + "/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = detail.GetProperty("version").GetUInt32(),
            action = "RetryHistoricalLease", reason = "Original period reopened",
        }, Ct);
        await ReadSuccessAsync(resolve);
        await using var db = Open(Migrator);
        var receipt = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        var posted = await db.Sales.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        Assert.Equal((receipt.ClaimedSoldAt, receipt.InvoiceNumber, receipt.PayloadHash),
            (posted.SoldAt, posted.InvoiceNumber, posted.RequestHash));
    }

    [Fact]
    public async Task Concurrent_reconnects_keep_one_original_sale_and_receipt()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var sale = await IssuedInvoiceAsync(client, actor, start, end, "valid");
        _clock.Now = end.AddMinutes(5);
        var responses = await Task.WhenAll(SyncAsync(client, sale), SyncAsync(client, sale.DeepClone()));
        Assert.Equal(["created", "duplicate"], responses.Select(x => x.GetProperty("status").GetString()).Order(StringComparer.Ordinal));
        await using var db = Open(Migrator);
        var receipt = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        var posted = await db.Sales.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        Assert.Equal(posted.Id, receipt.SaleId);
    }

    [Fact]
    public async Task Correcting_the_timestamp_of_a_held_invoice_cannot_replace_first_evidence()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var late = await IssuedInvoiceAsync(client, actor, start, end, "deadline");
        _clock.Now = end.AddMinutes(5);
        Assert.Equal("NeedsReview", (await SyncAsync(client, late)).GetProperty("status").GetString());
        var changed = late.DeepClone();
        changed["soldAt"] = end.AddMicroseconds(-1);
        var second = await SyncAsync(client, changed);
        Assert.Equal("NeedsReview", second.GetProperty("status").GetString());
        await using var db = Open(Migrator);
        Assert.Equal(2, await db.OfflineSubmissions.CountAsync(x => x.ShopId == actor.Shop, Ct));
        Assert.False(await db.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
    }

    [Fact]
    public async Task A_manager_replaying_a_late_submission_still_needs_the_original_permit()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var invoice = await IssuedInvoiceAsync(client, actor, start, end, "deadline");
        _clock.Now = end.AddDays(8);
        var result = await SyncAsync(client, invoice);
        var path = $"/api/offline-reconciliation/{result.GetProperty("reviewCaseId").GetGuid():D}";
        var detail = await client.GetFromJsonAsync<JsonElement>(path, Ct);
        using var resolve = await client.PostAsJsonAsync(path + "/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = detail.GetProperty("version").GetUInt32(),
            action = "RetryHistoricalLease", reason = "Verify original permit",
        }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode);
        var body = await resolve.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Reconciliation.ReplayRejected", body, StringComparison.Ordinal);
        await using var db = Open(Migrator);
        Assert.False(await db.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Device_clock_within_skew_allowance_posts_with_original_facts(int skewMinutes)
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        var firstReceipt = start.AddMinutes(10);
        var soldAt = firstReceipt.AddMinutes(skewMinutes);
        var busyOnce = 1;
        using var host = CreateHost(configure: services =>
        {
            services.RemoveAll<IShopLicenceFence>();
            services.AddScoped<IShopLicenceFence>(provider => new OneShotBusyRecoveryFence(
                new ShopLicenceFence(provider.GetRequiredService<ApplicationDbContext>(),
                    provider.GetRequiredService<ILicenceActionPolicy>(), _clock), () => Interlocked.Exchange(ref busyOnce, 0) == 1));
        });
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var sale = await IssuedInvoiceAsync(client, actor, start, end, "valid", soldAtOverride: soldAt);
        _clock.Now = firstReceipt;
        var initial = await SyncAsync(client, sale);
        Assert.Equal("failed", initial.GetProperty("status").GetString());
        Assert.Equal(Errors.Licence.BusyCode, initial.GetProperty("errors")[0].GetProperty("code").GetString());

        _clock.Now = soldAt.AddMinutes(1);
        var created = await SyncAsync(client, sale);
        Assert.Equal("created", created.GetProperty("status").GetString());
        await using var db = Open(Migrator);
        var envelope = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        var posted = await db.Sales.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        Assert.Equal(firstReceipt, envelope.ReceivedAt);
        Assert.NotNull(envelope.LicencePermitId);
        Assert.NotNull(envelope.LicenceGrantId);
        Assert.Equal((soldAt, sale["invoiceNumber"]!.GetValue<string>(), envelope.PayloadHash),
            (posted.SoldAt, posted.InvoiceNumber, posted.RequestHash));
    }

    [Fact]
    public async Task Device_clock_within_skew_allowance_replays_after_expiry_once_issue_time_is_historical()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        var firstReceipt = start.AddMinutes(10);
        var soldAt = firstReceipt.AddMinutes(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var sale = await IssuedInvoiceAsync(client, actor, start, end, "valid", soldAtOverride: soldAt);
        _clock.Now = firstReceipt;
        var held = await SyncAsync(client, sale);
        Assert.Equal("NeedsReview", held.GetProperty("status").GetString());
        Assert.Equal("Expiry.ReviewRequired", held.GetProperty("errors")[0].GetProperty("code").GetString());

        _clock.Now = end.AddMinutes(5);
        var path = $"/api/offline-reconciliation/{held.GetProperty("reviewCaseId").GetGuid():D}";
        var detail = await client.GetFromJsonAsync<JsonElement>(path, Ct);
        Assert.Contains("RetryHistoricalLease", detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()));
        using var resolve = await client.PostAsJsonAsync(path + "/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = detail.GetProperty("version").GetUInt32(),
            action = "RetryHistoricalLease", reason = "Device clock was within skew allowance",
        }, Ct);
        await ReadSuccessAsync(resolve);
        await using var db = Open(Migrator);
        var envelope = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        var posted = await db.Sales.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        Assert.Equal(firstReceipt, envelope.ReceivedAt);
        Assert.Equal((soldAt, sale["invoiceNumber"]!.GetValue<string>(), envelope.PayloadHash),
            (posted.SoldAt, posted.InvoiceNumber, posted.RequestHash));
    }

    [Fact]
    public async Task Device_clock_beyond_skew_allowance_holds_as_future_and_rejects_manager_replay()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        var firstReceipt = start.AddMinutes(10);
        var soldAt = firstReceipt.AddMinutes(5).AddMicroseconds(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var sale = await IssuedInvoiceAsync(client, actor, start, end, "valid", soldAtOverride: soldAt);
        _clock.Now = firstReceipt;
        var held = await SyncAsync(client, sale);
        Assert.Equal("NeedsReview", held.GetProperty("status").GetString());
        Assert.Equal(Errors.OfflineReconciliation.IssueTimeInFuture.Code, held.GetProperty("errors")[0].GetProperty("code").GetString());

        _clock.Now = end.AddMinutes(5);
        var path = $"/api/offline-reconciliation/{held.GetProperty("reviewCaseId").GetGuid():D}";
        var detail = await client.GetFromJsonAsync<JsonElement>(path, Ct);
        using var resolve = await client.PostAsJsonAsync(path + "/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = detail.GetProperty("version").GetUInt32(),
            action = "RetryHistoricalLease", reason = "Device clock exceeded skew allowance",
        }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode);
        var body = await resolve.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Reconciliation.ReplayRejected", body, StringComparison.Ordinal);
        Assert.Contains("OfflineLicenceHistory.OutsidePermitWindow", body, StringComparison.Ordinal);
        await using var db = Open(Migrator);
        var envelope = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        Assert.Equal(firstReceipt, envelope.ReceivedAt);
        Assert.False(await db.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
    }

    private sealed class OneShotBusyRecoveryFence(IShopLicenceFence inner, Func<bool> failNext) : IShopLicenceFence
    {
        public Task<ErrorOr<ShopLicenceLease>> EnterAsync(Guid shopId, LicenceAction action, CancellationToken cancellationToken) =>
            action == LicenceAction.Recovery && failNext()
                ? Task.FromResult<ErrorOr<ShopLicenceLease>>(Errors.Licence.Busy)
                : inner.EnterAsync(shopId, action, cancellationToken);
    }
}
