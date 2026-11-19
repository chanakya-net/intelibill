using System.Net;
using System.Text.Json;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408dLicensingTests
{
    private static readonly string[] RecoveryFields = ["name", "contactPerson", "mobileNumber"];

    [Theory]
    [InlineData("", "bank_accounts")]
    [InlineData("/setup", "shop_setup_preferences")]
    [InlineData("/setup/sample", "shop_expiry_states")]
    [InlineData("/setup/preferences", "shop_setup_preferences")]
    public async Task DeviceRevokedDuringAProtectedReadReceivesNoShopData(string route, string heldTable)
    {
        var email = $"p0408d-revoked-{Guid.NewGuid():N}@fixture.test";
        var (host, device, shop, _, _) = await OpenStockedShopAsync(email);
        using (host)
        using (device)
        {
            using var other = CreateClient(host);
            await LoginAsync(other, email);
            var session = ReadSession(device);
            await using var holder = Open(Migrator);
            await using var hold = await HoldTableAsync(holder, heldTable);

            var reading = device.GetAsync($"/api/shops/{shop}{route}", Ct);
            try
            {
                await WaitingBackendAsync(reading, Pid(holder));
                using var revoke = await other.DeleteAsync($"/api/users/me/sessions/{session.SessionId}", Ct);
                Assert.True(revoke.IsSuccessStatusCode, $"Revocation answered {(int)revoke.StatusCode}.");
            }
            finally
            {
                await hold.RollbackAsync(Ct);
                await Task.WhenAny(reading);
            }

            using var refused = await reading;
            var body = await refused.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Contains("Auth.SessionInvalidated", body);
            AssertNoShopData(body);
        }
    }

    [Theory]
    [InlineData(MembershipStatus.Removed, ShopRole.Owner, HttpStatusCode.Forbidden, "Shop.MembershipNotFound")]
    [InlineData(MembershipStatus.Active, ShopRole.Staff, HttpStatusCode.Conflict, "Licence.Busy")]
    public async Task MembershipChangedDuringASelectedShopReadReceivesNoShopData(
        MembershipStatus status, ShopRole role, HttpStatusCode expected, string code)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var device = CreateClient(host);
        await RegisterAsync(device);
        var assigned = await CreateShopAsync(device, register: false);
        var active = await CreateShopAsync(device, register: false);
        var actor = ReadSession(device).UserId;
        await using var holder = Open(Migrator);
        await using var hold = await HoldTableAsync(holder, "bank_accounts");

        var reading = device.GetAsync($"/api/shops/{assigned}", Ct);
        try
        {
            await WaitingBackendAsync(reading, Pid(holder));
            await using var authority = Open(Migrator);
            var membership = await authority.ShopMemberships.SingleAsync(x => x.ShopId == assigned && x.UserId == actor, Ct);
            membership.ChangeAuthority(role, status);
            await authority.SaveChangesAsync(Ct);
        }
        finally
        {
            await hold.RollbackAsync(Ct);
            await Task.WhenAny(reading);
        }

        using var refused = await reading;
        var body = await refused.Content.ReadAsStringAsync(Ct);
        Assert.Equal(expected, refused.StatusCode);
        Assert.Contains(code, body);
        AssertNoShopData(body);

        using var unrelated = await device.GetAsync($"/api/shops/{active}", Ct);
        Assert.Equal(active, (await ReadSuccessAsync(unrelated)).GetProperty("shopId").GetGuid());
        using var retried = await device.GetAsync($"/api/shops/{assigned}", Ct);
        if (status != MembershipStatus.Active)
        {
            await AssertProblemAsync(retried, HttpStatusCode.Forbidden, "Shop.MembershipNotFound");
            return;
        }
        var access = (await ReadSuccessAsync(retried)).GetProperty("setupAccess");
        Assert.Empty(access.GetProperty("writableProfileFields").EnumerateArray());
        Assert.False(access.GetProperty("canPrepareTax").GetBoolean());
    }

    [Fact]
    public async Task SetupReadsAreBoundToTheDevicesActiveShop()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var device = CreateClient(host);
        await RegisterAsync(device);
        var assigned = await CreateShopAsync(device, register: false);
        var active = await CreateShopAsync(device, register: false);
        var session = ReadSession(device);
        Assert.Equal(active, session.ActiveShopId);
        var forged = session with { ActiveShopId = assigned };

        foreach (var (shop, claims, code) in new[]
        {
            (assigned, session, "ShopSetup.Forbidden"),
            (assigned, forged, "Auth.SessionInvalidated"),
        })
        {
            var status = await DirectAsync(host, device, services => Handler<GetShopSetupQueryHandler>(services)
                .HandleAsync(new(shop, session.UserId, shop) { Session = claims }, Ct));
            var sample = await DirectAsync(host, device, services => Handler<GetShopSetupSampleQueryHandler>(services)
                .HandleAsync(new(shop, session.UserId, shop) { Session = claims }, Ct));
            var preferences = await DirectAsync(host, device, services => Handler<GetShopSetupPreferencesQueryHandler>(services)
                .HandleAsync(new(shop, session.UserId, shop) { Session = claims }, Ct));
            Assert.Equal(code, status.FirstError.Code);
            Assert.Equal(code, sample.FirstError.Code);
            Assert.Equal(code, preferences.FirstError.Code);
        }

        var own = await DirectAsync(host, device, services => Handler<GetShopSetupQueryHandler>(services)
            .HandleAsync(new(active, session.UserId, active) { Session = session }, Ct));
        var ownSample = await DirectAsync(host, device, services => Handler<GetShopSetupSampleQueryHandler>(services)
            .HandleAsync(new(active, session.UserId, active) { Session = session }, Ct));
        var ownPreferences = await DirectAsync(host, device, services => Handler<GetShopSetupPreferencesQueryHandler>(services)
            .HandleAsync(new(active, session.UserId, active) { Session = session }, Ct));
        Assert.False(own.IsError, own.IsError ? own.FirstError.Code : null);
        Assert.Equal(active, own.Value.ShopId);
        Assert.Equal(active, own.Value.SetupAccess!.ShopId);
        Assert.Equal("ShopSetup.SampleUnavailable", ownSample.FirstError.Code);
        Assert.False(ownPreferences.IsError, ownPreferences.IsError ? ownPreferences.FirstError.Code : null);

        var detail = await ReadDetailsAsync(host, device, new(session.UserId, assigned) { Session = session });
        Assert.False(detail.IsError, detail.IsError ? detail.FirstError.Code : null);
        Assert.Equal(assigned, detail.Value.ShopId);
    }

    [Fact]
    public async Task ReadsCrossingTheTradingEndReportRecoveryAccess()
    {
        var (host, client, shop, _, end) = await OpenStockedShopAsync();
        using (host)
        using (client)
        {
            await using var holder = Open(Migrator);
            await using var hold = await HoldTableAsync(holder, "bank_accounts");
            await holder.Database.ExecuteSqlAsync($"SELECT 1 FROM shop_expiry_states WHERE shop_id = {shop} FOR UPDATE", Ct);

            var status = client.GetAsync($"/api/shops/{shop}/setup", Ct);
            var details = client.GetAsync($"/api/shops/{shop}", Ct);
            try
            {
                await AssertWaitersAsync(Pid(holder), 2, status, details);
                _clock.Now = end;
            }
            finally
            {
                await hold.RollbackAsync(Ct);
                await Task.WhenAll(status, details);
            }

            using var served = await status;
            var setup = await ReadSuccessAsync(served);
            AssertRecoveryAccess(setup.GetProperty("setupAccess"));
            var permissions = setup.GetProperty("permissions");
            Assert.True(permissions.GetProperty("canEditShop").GetBoolean());
            Assert.False(permissions.GetProperty("canEditCatalog").GetBoolean());
            Assert.False(permissions.GetProperty("canSavePreferences").GetBoolean());
            Assert.False(setup.GetProperty("preferences").GetProperty("canSavePreferences").GetBoolean());
            using var detailed = await details;
            var detail = await ReadSuccessAsync(detailed);
            Assert.Equal("P04-08d merchant", detail.GetProperty("name").GetString());
            AssertRecoveryAccess(detail.GetProperty("setupAccess"));

            using var sample = await client.GetAsync($"/api/shops/{shop}/setup/sample", Ct);
            Assert.True((await ReadSuccessAsync(sample)).GetProperty("isSample").GetBoolean());
        }
    }

    private static void AssertRecoveryAccess(JsonElement access)
    {
        Assert.Equal("Expired", access.GetProperty("status").GetString());
        Assert.Equal(RecoveryFields, access.GetProperty("writableProfileFields").EnumerateArray().Select(x => x.GetString()));
        foreach (var permission in new[] { "canPrepareTax", "canSavePreferences", "canEditOperatingSettings", "hasOperatingAccess", "canCreateAdditionalShop" })
            Assert.False(access.GetProperty(permission).GetBoolean(), permission);
    }

    private static void AssertNoShopData(string body)
    {
        foreach (var field in new[] { "P04-08d merchant", "42 MG Road", "\"document\"", "\"steps\"", "\"showOnStartup\"", "\"setupAccess\"" })
            Assert.DoesNotContain(field, body);
    }

    private static T Handler<T>(IServiceProvider services) => ActivatorUtilities.CreateInstance<T>(services);

    private static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> HoldTableAsync(
        Intelibill.Infrastructure.Data.ApplicationDbContext db, string table)
    {
        var transaction = await db.Database.BeginTransactionAsync(Ct);
        // The table name comes from the test's own theory data.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"LOCK TABLE public.{table} IN ACCESS EXCLUSIVE MODE", Ct);
#pragma warning restore EF1002
        return transaction;
    }

    private async Task AssertWaitersAsync(int blocker, int expected, params Task[] operations)
    {
        await using var observer = Open(Migrator);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!operations.Any(x => x.IsCompleted))
        {
            var waiting = await observer.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE wait_event_type = 'Lock' AND {blocker} = ANY(pg_blocking_pids(pid))
                """).SingleAsync(deadline.Token);
            if (waiting == expected) return;
            await Task.Delay(10, deadline.Token);
        }
        Assert.Fail("A read completed before reaching its expected database wait.");
    }
}
