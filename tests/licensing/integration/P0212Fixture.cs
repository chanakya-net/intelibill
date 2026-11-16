using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Local P02-12 fixture: one licensing host on its own migrated database with a real Licensing Manager signed in
/// with password + TOTP. The lifecycle (status) clock is fixed and separate from the session/MFA clock, so grant
/// boundaries are evaluated at exact instants without forging session times.
/// </summary>
internal sealed class P0212Fixture(P0206Fixture provider, P0201Clock lifecycle) : IAsyncDisposable
{
    internal static readonly DateTimeOffset Now = P0209Fixture.Utc("2027-03-01T00:00:00Z");
    // 1 March 2027 00:00 IST to 1 April 2027 00:00 IST, end-exclusive.
    internal static readonly DateTimeOffset Starts = P0209Fixture.Utc("2027-02-28T18:30:00Z");
    internal static readonly DateTimeOffset Ends = P0209Fixture.Utc("2027-03-31T18:30:00Z");

    internal P0206Fixture Provider { get; } = provider;
    internal P0201Clock Lifecycle { get; } = lifecycle;
    internal NpgsqlConnectionStringBuilder Owner => Provider.Superuser;

    internal static async Task<P0212Fixture> StartAsync(LicensingPostgreSqlFixture database, DateTimeOffset? at = null,
        IReadOnlyDictionary<string, string?>? overrides = null, bool publishEntitlements = true)
    {
        var lifecycle = new P0201Clock { Now = at ?? Now };
        var provider = await P0206Fixture.StartAsync(database, overrides,
            services =>
            {
                services.AddKeyedSingleton<TimeProvider>(P0209Fixture.StatusClockKey, lifecycle);
                if (!publishEntitlements) services.AddPreEntitlementPublisher();
            });
        return new(provider, lifecycle);
    }

    internal Task<(P0110CookieBrowser Manager, string Seed)> ManagerAsync() => Provider.ManagerAsync();

    /// <summary>An account with <paramref name="shops"/> affiliated shops and no coverage term of their own.</summary>
    internal async Task<(Guid Account, Guid Payer, Guid[] Shops)> SeedAccountAsync(int shops)
    {
        var (account, payer) = await P0209Fixture.SeedAccountAsync(Owner);
        var ids = new Guid[shops];
        for (var index = 0; index < shops; index++)
        {
            ids[index] = Guid.NewGuid();
            await P0209Fixture.AffiliateAsync(Owner, account, payer, ids[index], Guid.NewGuid(), null,
                Now.AddDays(-30), null);
        }
        return (account, payer, ids);
    }

    /// <summary>A shop affiliated with the account, optionally assigned its own seeded baseline term.</summary>
    internal async Task<Guid> AffiliatedShopAsync(Guid account, Guid payer, P0209Fixture.TermSeed? seed = null)
    {
        var shop = Guid.NewGuid();
        Guid? term = seed is null ? null : await P0209Fixture.SeedTermAsync(Owner, account, seed);
        await P0209Fixture.AffiliateAsync(Owner, account, payer, shop, Guid.NewGuid(), term,
            seed?.StartsAt ?? Now.AddDays(-30), seed?.EndsAt);
        return shop;
    }

    internal static async Task<JsonElement> GrantAsync(P0110CookieBrowser manager, Guid account, object draft, Guid? key = null)
    {
        var preview = await PreviewAsync(manager, account, draft);
        using var response = await PostAsync(manager, Base(account), Confirm(preview, key ?? Guid.NewGuid()));
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await P0206Fixture.BodyAsync(response);
    }

    internal static string Base(Guid account) => $"/api/admin/accounts/{account:D}/grants/complimentary";

    internal static object Draft(Guid[] shops, DateTimeOffset? starts = null, DateTimeOffset? ends = null,
        int memberAllowance = 4, string reason = "Launch partner courtesy access") => new
    {
        shopIds = shops,
        startsAt = (starts ?? Starts).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        endsAt = (ends ?? Ends).ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        memberAllowance,
        reason,
    };

    internal static object Confirm(JsonElement preview, Guid operationKey, bool confirmed = true) => new
    {
        operationKey,
        shopIds = preview.GetProperty("shopIds").EnumerateArray().Select(item => item.GetGuid()).ToArray(),
        startsAt = preview.GetProperty("startsAt").GetString(),
        endsAt = preview.GetProperty("endsAt").GetString(),
        memberAllowance = preview.GetProperty("memberAllowance").GetInt32(),
        reason = preview.GetProperty("reason").GetString(),
        expectedVersion = preview.GetProperty("expectedVersion").GetInt64(),
        sourceHash = preview.GetProperty("sourceHash").GetString(),
        confirmed,
    };

    internal static async Task<HttpResponseMessage> PostAsync(P0110CookieBrowser browser, string path, object body,
        string origin = P0110CookieBrowser.Origin) =>
        await browser.SendAsync(HttpMethod.Post, path, body, await P0206Fixture.ProofAsync(browser), origin);

    internal static async Task<HttpResponseMessage> PostRawAsync(P0110CookieBrowser browser, string path, string json) =>
        await browser.SendAsync(HttpMethod.Post, path, new StringContent(json, Encoding.UTF8, "application/json"),
            await P0206Fixture.ProofAsync(browser));

    internal static async Task<JsonElement> PreviewAsync(P0110CookieBrowser manager, Guid account, object draft)
    {
        using var response = await PostAsync(manager, Base(account) + "/preview", draft);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    internal static async Task<JsonElement> ShopStatusAsync(P0110CookieBrowser reader, Guid account, Guid shop)
    {
        using var response = await reader.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{account:D}/shops/{shop:D}/status");
        return await P0206Fixture.BodyAsync(response);
    }

    internal async Task<long> CountAsync(string sql) => (long)(await Provider.ScalarAsync(sql))!;

    public ValueTask DisposeAsync() => Provider.DisposeAsync();
}
