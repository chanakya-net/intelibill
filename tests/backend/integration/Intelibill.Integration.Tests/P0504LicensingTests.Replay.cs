using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

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
}
