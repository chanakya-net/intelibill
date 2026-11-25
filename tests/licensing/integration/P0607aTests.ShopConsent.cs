using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607aTests
{
    [Fact]
    public async Task ShopConsentSurvivesRetriesWithoutChangingAccess()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var before = await s.BaselineAsync();
        var id = (await CreateAsync(s)).GetProperty("transferId").GetGuid();
        var path = ShopConsentPath(s.Source.Account, s.North.ShopId, id);
        var north = s.North.Client;

        // The named shop's owner sees their shop, the parties, dates and treatment; nothing about siblings or the reason.
        using var read = await north.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var raw = await read.Content.ReadAsStringAsync();
        var shown = JsonSerializer.Deserialize<JsonElement>(raw);
        Assert.Equal(("North", "AwaitingConsent", "None"), (shown.GetProperty("shopName").GetString(),
            shown.GetProperty("consentStatus").GetString(), shown.GetProperty("delivery").GetString()));
        Assert.True(shown.GetProperty("disconnectedQueuesUnknown").GetBoolean());
        foreach (var hidden in new[] { "Central", "East", "Branch sold", s.Term.ToString() })
            Assert.DoesNotContain(hidden, raw, StringComparison.Ordinal);

        // Billing owners and other shop owners cannot decide for the named shop; stale terms are refused.
        foreach (var client in new[] { s.Source.Payer.Client, s.Destination.Payer.Client, s.Source.Shops[1].Client })
        {
            using var denied = await DecideAsync(client, path, "accept", shown);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var stale = await DecideAsync(north, path, "accept", shown, termsDigest: new string('0', 64));
        Assert.Equal("Coverage.TransferChanged", await CodeAsync(stale));

        // Accepted: the event and its outbox message commit together; licensing has nothing until delivery.
        var key = Guid.NewGuid();
        using var accepted = await DecideAsync(north, path, "accept", shown, key);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("Pending", (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("delivery").GetString());
        using var retried = await DecideAsync(north, path, "accept", shown, key);
        using var duplicate = await DecideAsync(north, path, "accept", shown);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("Coverage.TransferConsentInvalid", await CodeAsync(duplicate));
        Assert.Equal(1L, await MerchantCountAsync(s, "coverage_transfer_consent_events", $"transfer_id='{id}'"));
        Assert.Equal(1L, await MerchantCountAsync(s, "entitlement_messages", "kind='CoverageTransferConsent'"));
        Assert.Equal(0L, await s.F.CountAsync("coverage_transfer_consents"));

        await s.F.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_consents", "purpose='ShopOwner' AND kind='Accepted' AND generation=1"));
        Assert.Equal("Recorded", (await north.GetFromJsonAsync<JsonElement>(path)).GetProperty("delivery").GetString());
        Assert.Equal("Accepted", Status(await ReadAsync(s.Source.Payer.Client, s.Source.Account, id), "ShopOwner"));

        await AssertRedeliveryAndReorderingAreSafeAsync(s, id, path);
        Assert.Equal(before, await s.BaselineAsync());
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, RequestedAt));
    }

    private static async Task AssertRedeliveryAndReorderingAreSafeAsync(Scenario s, Guid id, string path)
    {
        var north = s.North.Client;
        // Duplicate delivery of the same event records nothing new.
        await RequeueAsync(s, "true");
        await s.F.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_consents"));

        // Revoke then accept again, but deliver the later acceptance first and the revocation last.
        using var revoked = await DecideAsync(north, path, "revoke", await north.GetFromJsonAsync<JsonElement>(path));
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var again = await DecideAsync(north, path, "accept", await north.GetFromJsonAsync<JsonElement>(path));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using var late = await DecideAsync(north, path, "revoke", await north.GetFromJsonAsync<JsonElement>(path));
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        await HoldAsync(s, "e.kind='Revoked'");
        await s.F.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("Accepted", Status(await ReadAsync(s.Source.Payer.Client, s.Source.Account, id), "ShopOwner"));
        await RequeueAsync(s, "e.kind='Revoked'");
        await s.F.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("Revoked", Status(await ReadAsync(s.Source.Payer.Client, s.Source.Account, id), "ShopOwner"));

        // The already-delivered generation-two acceptance arriving again cannot reactivate the revoked consent.
        await RequeueAsync(s, "e.kind='Accepted'");
        await s.F.Checkout.Delivery.DrainAsync(false);
        Assert.Equal("Revoked", Status(await ReadAsync(s.Source.Payer.Client, s.Source.Account, id), "ShopOwner"));
        Assert.Equal(4L, await s.F.CountAsync("coverage_transfer_consents", "purpose='ShopOwner'"));
        Assert.Equal(0L, await MerchantCountAsync(s, "entitlement_outbox", "status='DeadLetter'"));
    }

    private static async Task<long> MerchantCountAsync(Scenario s, string table, string where) =>
        (long)(await s.F.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM public.{table} WHERE {where}"))!;

    private static string OutboxFilter(string consent) => $"""
        FROM public.coverage_transfer_consent_events e
        WHERE e.id = o.event_id AND ({consent})
        """;

    private static Task RequeueAsync(Scenario s, string consent) => LicensingPostgreSqlFixture.ExecuteAsync(
        s.F.Hosts.Database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), $"""
            UPDATE public.entitlement_outbox o SET status = 'Pending', attempts = 0, next_attempt_at = '{RequestedAt:O}'::timestamptz
            {OutboxFilter(consent)}
            """);

    private static Task HoldAsync(Scenario s, string consent) => LicensingPostgreSqlFixture.ExecuteAsync(
        s.F.Hosts.Database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), $"""
            UPDATE public.entitlement_outbox o SET next_attempt_at = '{RequestedAt.AddDays(30):O}'::timestamptz
            {OutboxFilter(consent)} AND o.status = 'Pending'
            """);
}
