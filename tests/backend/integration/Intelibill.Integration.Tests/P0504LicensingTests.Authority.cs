using System.Net;
using System.Net.Http.Json;
using ErrorOr;
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
    public async Task Suspension_committed_between_first_receipt_and_posting_prevents_the_sale()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = CreateHost(configure: services =>
        {
            services.RemoveAll<IShopLicenceFence>();
            services.AddScoped<IShopLicenceFence>(provider => new PausedRecoveryFence(
                new ShopLicenceFence(provider.GetRequiredService<ApplicationDbContext>(),
                    provider.GetRequiredService<ILicenceActionPolicy>(), _clock), reached, resume));
        });
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var sale = await IssuedInvoiceAsync(client, actor, start, end, "valid");
        _clock.Now = end.AddMinutes(5);
        var sync = client.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = Device, sales = new[] { sale } }, Ct);
        await using var db = Open(Migrator);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
            Assert.Equal(1, await db.OfflineSubmissions.CountAsync(s => s.ShopId == actor.Shop, Ct));
            await ApplyAsync(actor.Binding, end.AddDays(1), revision: 2, restriction: "Security");
        }
        finally { resume.TrySetResult(); }
        using var response = await sync;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await db.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
    }

    [Fact]
    public async Task Reauthenticated_issuer_can_recover_but_the_revoked_session_cannot_replay()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var original = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(original, start, end);
        var sale = await IssuedInvoiceAsync(original, actor, start, end, "valid");
        using var current = await CreateSecondSessionClientAsync(host, actor, end.AddDays(1));
        _clock.Now = end.AddMinutes(5);
        using var revoke = await current.DeleteAsync($"/api/users/me/sessions/{actor.Session:D}", Ct);
        Assert.True(revoke.IsSuccessStatusCode);
        Assert.Equal("created", (await SyncAsync(current, sale)).GetProperty("status").GetString());
        using var replay = await original.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = Device, sales = new[] { sale } }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    private sealed class PausedRecoveryFence(IShopLicenceFence inner, TaskCompletionSource reached,
        TaskCompletionSource resume) : IShopLicenceFence
    {
        public async Task<ErrorOr<ShopLicenceLease>> EnterAsync(Guid shopId, LicenceAction action, CancellationToken cancellationToken)
        {
            if (action == LicenceAction.Recovery)
            {
                reached.TrySetResult();
                await resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return await inner.EnterAsync(shopId, action, cancellationToken);
        }
    }
}
