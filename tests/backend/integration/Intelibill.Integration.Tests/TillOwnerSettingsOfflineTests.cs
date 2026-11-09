using System.Net;
using System.Text.Json;
using Intelibill.Application.Features.Shops.TillSettings;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class TillOfflineReconciliationTests
{
    [Fact]
    public async Task Disable_DuringIncomingOfflineEnvelope_HoldsLateInvoiceForReview()
    {
        using var client = CreateClient();
        var (owner, payload, session) = await ArrangeAsync(client);
        await CloseAsync(owner, session);
        await using var app = new TillTestContext(fixture.MigratorConnectionString, owner.ShopId);
        var originalClose = JsonSerializer.Serialize(await app.Db.TillCloses.AsNoTracking().SingleAsync(x => x.SessionId == session.Id));
        await using var barrier = await TillPostgresBarrier.CreateAsync(fixture.MigratorConnectionString, "shop_till_setting_events", owner.ShopId);
        var disabling = SendAsync(client, HttpMethod.Put, $"/api/shops/{owner.ShopId}/till-settings", owner.Token,
            new { enabled = false, expectedVersion = 0, reason = "Device queues checked at setup change", confirmed = true });
        var changerPid = await barrier.WaitForArrivalAsync();
        // Admission is intentionally durable first. An envelope arriving after the readiness
        // check is retained while its poster waits for the exclusive settings transition.
        var incoming = SyncAsync(client, owner.Token, payload);
        await barrier.WaitForBlockedByAsync(changerPid);
        var envelope = Assert.Single(await app.Db.OfflineSubmissions.AsNoTracking().Where(x => x.ShopId == owner.ShopId).ToArrayAsync());
        Assert.Null(envelope.SaleId);
        Assert.False(incoming.IsCompleted);
        await barrier.ReleaseAsync();
        using var disabled = await disabling.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var held = await incoming.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("NeedsReview", held.GetProperty("status").GetString());
        var detail = await GetCaseAsync(client, owner.Token, held.GetProperty("reviewCaseId").GetGuid());
        Assert.Equal("TillReview", detail.GetProperty("issueType").GetString());
        Assert.Contains("Till.Disabled", Strings(detail.GetProperty("reasonCodes")));
        Assert.False(await app.Db.Sales.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.False(await app.Db.TillMovements.AnyAsync(x => x.ShopId == owner.ShopId));
        Assert.Equal(originalClose, JsonSerializer.Serialize(await app.Db.TillCloses.AsNoTracking().SingleAsync(x => x.SessionId == session.Id)));
        using var read = await SendAsync(client, HttpMethod.Get, $"/api/shops/{owner.ShopId}/till-settings", owner.Token);
        read.EnsureSuccessStatusCode();
        var settings = JsonSerializer.Deserialize<ShopTillSettingsDto>(await read.Content.ReadAsStringAsync(), JsonSerializerOptions.Web)!;
        Assert.False(settings.Enabled);
        Assert.Equal(1, settings.PendingOfflineCount);
    }
}
