using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FinalConsentEpochAndConsumeReceiptSerializeInBothOrders(bool applyFirst)
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var id = await ConsentedAsync(s, s.Permit.ValidUntil);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("race@example.test");
        using var session = manager;
        var key = await ApproveAsync(s, id, manager);
        await DrainAsync(s);
        var delivery = s.F.Checkout.Delivery;
        s.F.At(s.Permit.ValidUntil);
        s.F.Hosts.Fixture.Clock.Now = s.Permit.ValidUntil;
        delivery.Transport.LeaseClock.Now = s.Permit.ValidUntil.AddSeconds(1);
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        Assert.Equal(1L, await delivery.MerchantScalarAsync($"SELECT count(*) FROM merchant_transfer_decisions WHERE transfer_id='{id}' AND outcome='Consumed'"));
        // Isolate this signed receipt from unrelated verification traffic at the same clock boundary.
        await LicensingPostgreSqlFixture.ExecuteAsync(delivery.MerchantDb, $"""
            UPDATE entitlement_outbox o SET next_attempt_at='2030-01-01' FROM entitlement_messages m
            WHERE m.event_id=o.event_id AND m.kind='Acknowledgement' AND m.account_id IN ('{s.Source.Account}','{s.Destination.Account}') AND o.status='Pending'
            """);
        await RaceFinalReceiptAsync(s, applyFirst);
        await DrainAsync(s);
        await AssertStateAsync(manager, AdminPath(s, id), key, applyFirst ? "Applied" : "NeedsReview");
        Assert.Equal(applyFirst ? s.Destination.Account : s.Source.Account, await BindingAsync(s));
        Assert.Equal(applyFirst ? 1L : 0L, await s.F.CountAsync("coverage_transfer_bindings"));
    }

    [Theory]
    [InlineData("authority-version")]
    [InlineData("shop-owner-revoke")]
    public async Task PostConsumeShopOwnerChangeOrRevokeNeverOrphansBinding(string mutation)
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var id = await ConsentedAsync(s, s.Permit.ValidUntil);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("post-consume@example.test");
        using var session = manager;
        var key = await ApproveAsync(s, id, manager);
        await DrainAsync(s);
        await AssertStateAsync(manager, AdminPath(s, id), key, "Scheduled");

        var delivery = s.F.Checkout.Delivery;
        s.F.At(s.Permit.ValidUntil);
        s.F.Hosts.Fixture.Clock.Now = s.Permit.ValidUntil;
        delivery.Transport.LeaseClock.Now = s.Permit.ValidUntil.AddSeconds(1);
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        Assert.Equal(1L, await delivery.MerchantScalarAsync(
            $"SELECT count(*) FROM merchant_transfer_decisions WHERE transfer_id='{id}' AND outcome='Consumed'"));

        s.F.Hosts.Fixture.Clock.Now = s.Permit.ValidUntil.AddSeconds(1);
        var revoke = await MutateAfterConsumeAsync(s, id, mutation);
        await DrainAsync(s);
        await DrainAsync(s);

        var state = await OperationStateAsync(manager, AdminPath(s, id), key);
        var bindings = await s.F.CountAsync("coverage_transfer_bindings");
        var account = (Guid)(await BindingAsync(s))!;
        var trading = await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, s.Permit.ValidUntil);
        Assert.False(bindings == 1L && account == s.Source.Account,
            $"Orphaned binding after post-consume {mutation}: state={state}, bindings={bindings}, account={account}, trading={trading}");
        if (bindings == 1L)
        {
            Assert.Equal("Applied", state);
            Assert.Equal(s.Destination.Account, account);
            if (revoke is { } r)
            {
                Assert.Equal(HttpStatusCode.Conflict, r.Status);
                Assert.Equal("Coverage.TransferChanged", r.Code);
                Assert.Equal("PendingActivation", r.ShownState);
            }
        }
        else
        {
            Assert.Equal("NeedsReview", state);
            Assert.Equal(0L, bindings);
            Assert.Equal(s.Source.Account, account);
        }
        Assert.Equal("Allowed", trading);
    }

    private static async Task<(HttpStatusCode Status, string? Code, string? ShownState)?> MutateAfterConsumeAsync(
        Scenario s, Guid id, string mutation)
    {
        if (mutation == "authority-version")
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(s.F.Checkout.Delivery.MerchantDb, $"""
                UPDATE public.shop_memberships SET authority_version = authority_version + 1
                WHERE shop_id = '{s.North.ShopId}' AND user_id = '{s.North.UserId}'
                """);
            return null;
        }

        using var owner = s.F.Hosts.Merchant.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await P0201Tests.LoginAsync(owner, s.North.Email));
        var path = ShopConsentPath(s.Source.Account, s.North.ShopId, id);
        var shown = await owner.GetFromJsonAsync<JsonElement>(path);
        var shownState = shown.GetProperty("state").GetString();
        using var revoked = await DecideAsync(owner, path, "revoke", shown);
        var code = revoked.StatusCode == HttpStatusCode.Conflict ? await CodeAsync(revoked) : null;
        Assert.True(revoked.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            await revoked.Content.ReadAsStringAsync());
        return (revoked.StatusCode, code, shownState);
    }

    private static async Task<string?> OperationStateAsync(P0110CookieBrowser manager, string path, Guid operation)
    {
        using var result = await manager.SendAsync(HttpMethod.Get, path + $"/operations/{operation}");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        return (await P0206Fixture.BodyAsync(result)).GetProperty("state").GetString();
    }

    private static async Task RaceFinalReceiptAsync(Scenario s, bool applyFirst)
    {
        var account = s.Source.Account;
        var acquire = $"SELECT pg_advisory_xact_lock(hashtextextended('b{account:D}',0))";
        await using var gate = new NpgsqlConnection(s.F.Hosts.LicensingOwner.ConnectionString);
        await gate.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand(acquire, gate, tx);
        await hold.ExecuteNonQueryAsync();
        Task Receipt() => s.F.Checkout.Delivery.DrainAsync(false);
        Task RotateAuthority() => LicensingPostgreSqlFixture.ExecuteAsync(s.F.Hosts.LicensingOwner, $"""
            BEGIN;
            {acquire};
            UPDATE billing_account_members SET revision=revision+1
            WHERE account_id='{account}' AND merchant_user_id='{s.Source.Payer.UserId}';
            COMMIT
            """);
        var first = applyFirst ? Receipt() : RotateAuthority();
        await WaitForBlockedAsync(s, 1);
        var second = applyFirst ? RotateAuthority() : Receipt();
        await WaitForBlockedAsync(s, 2);
        await tx.CommitAsync();
        await Task.WhenAll(first, second);
    }

    private static async Task WaitForBlockedAsync(Scenario s, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while ((long)(await s.F.ScalarAsync("""
            SELECT count(*) FROM pg_stat_activity WHERE datname=current_database()
                AND wait_event='advisory' AND cardinality(pg_blocking_pids(pid))>0
            """))! < count)
            await Task.Delay(10, timeout.Token);
    }
}
