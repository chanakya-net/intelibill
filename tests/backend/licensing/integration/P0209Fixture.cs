using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Licensing.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Local P02-09 fixture: real PostgreSQL for both services, real merchant sign-in and billing accounts, and
/// explicit fixture term facts. The status evaluation clock is separate from the clocks that sign and verify
/// delegations, so a fixed calendar instant can be evaluated without forging session times.
/// </summary>
internal static class P0209Fixture
{
    internal const string StatusClockKey = "subscription-status";

    internal sealed record Hosts(P0201Fixture Fixture, LicensingApiFactory Licensing, MerchantStartupProbeFactory Merchant,
        P0201Clock StatusClock, LicensingPostgreSqlFixture Database,
        bool OwnsLicensing = true, bool OwnsMerchant = true, Func<ValueTask>? OnDispose = null) : IAsyncDisposable
    {
        internal NpgsqlConnectionStringBuilder LicensingOwner => Database.Bootstrap(Fixture.Owner.Database);

        public async ValueTask DisposeAsync()
        {
            if (OnDispose is not null)
                await OnDispose();
            if (OwnsMerchant)
                await Merchant.DisposeAsync();
            if (OwnsLicensing)
                await Licensing.DisposeAsync();
            Fixture.Dispose();
        }
    }

    internal sealed record Merchant(Guid UserId, Guid ShopId, HttpClient Client, string Email);

    internal static async Task<Hosts> StartAsync(LicensingPostgreSqlFixture database, DateTimeOffset evaluatedAt,
        Func<HttpMessageHandler, HttpMessageHandler>? transport = null, int licensingPoolSize = 1)
    {
        await database.PrepareMerchantAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = P0202Fixture.WholeSecondNow();
        var statusClock = new P0201Clock { Now = evaluatedAt };
        var settings = fixture.LicensingSettings();
        settings["LicensingDatabase:MaxPoolSize"] = licensingPoolSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var licensing = P0202Fixture.LicensingApi(fixture, configure: services =>
            services.AddKeyedSingleton<TimeProvider>(StatusClockKey, statusClock), settings: settings);
        var merchant = fixture.MerchantApi(
            handler: () => transport?.Invoke(licensing.Server.CreateHandler()) ?? licensing.Server.CreateHandler(),
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock));
        return new Hosts(fixture, licensing, merchant, statusClock, database);
    }

    /// <summary>A verified merchant signed in through the real login route, optionally holding a shop role.</summary>
    internal static async Task<Merchant> SignInAsync(Hosts host, ShopRole? role = null, Guid? existingShop = null)
    {
        var email = P0202Fixture.UniqueEmail();
        var user = await P0201Tests.SeedMerchantAsync(host.Merchant.Services, email);
        var shopId = Guid.Empty;
        if (role is { } shopRole)
        {
            await using var db = MerchantDb(host.Database);
            if (existingShop is { } shared)
                shopId = shared;
            else
            {
                var shop = Shop.Create("Status Fixture Shop", "Fixture Road", "Bengaluru", "Karnataka", "560001", null, null, null);
                db.Shops.Add(shop);
                shopId = shop.Id;
            }
            db.ShopMemberships.Add(ShopMembership.Create(shopId, user, shopRole, true));
            await db.SaveChangesAsync();
        }

        var client = host.Merchant.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(client, email));
        return new Merchant(user, shopId, client, email);
    }

    /// <summary>An owner whose shop is created through the real shop route, so every trading prerequisite exists.</summary>
    internal static async Task<Merchant> CreateTradingShopAsync(Hosts host)
    {
        var owner = await SignInAsync(host);
        var name = "Status Trading " + Guid.NewGuid().ToString("N")[..8];
        using var created = await owner.Client.PostAsJsonAsync("/api/shops", new
        {
            name, address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        owner.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        await using var db = MerchantDb(host.Database);
        var shop = await db.Shops.Where(row => row.Name == name).Select(row => row.Id).SingleAsync();
        return owner with { ShopId = shop };
    }

    internal static ApplicationDbContext MerchantDb(LicensingPostgreSqlFixture database) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase).ConnectionString)
            .UseSnakeCaseNamingConvention().Options);

    internal static async Task<Guid> CreateAccountAsync(HttpClient payer, string name = "Status Fixture Payer")
    {
        var request = new { operationKey = Guid.NewGuid(), name };
        using var first = await payer.PostAsJsonAsync("/api/billing/accounts", request);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        if (first.StatusCode == HttpStatusCode.ServiceUnavailable &&
            body.TryGetProperty("code", out var code) && code.GetString() == "Billing.DelegationUnavailable")
        {
            // The original write may have committed. Replay the same operation once to learn its outcome.
            using var replay = await payer.PostAsJsonAsync("/api/billing/accounts", request);
            var result = await replay.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(replay.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                $"Account setup replay returned {(int)replay.StatusCode}: {result}");
            return result.GetProperty("accountId").GetGuid();
        }

        Assert.True(first.StatusCode == HttpStatusCode.Created, $"Account setup returned {(int)first.StatusCode}: {body}");
        return body.GetProperty("accountId").GetGuid();
    }

    /// <summary>Explicit local term facts: the supplement row the reader evaluates. Null basis seeds coverage only.</summary>
    internal sealed record TermSeed(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Basis = "Paid",
        string Period = "Month", string AnchorLocal = "", int Ordinal = 0, int MemberAllowance = 5,
        long? PricePaise = 149900, bool Grouped = false, int Capacity = 1, string? Zone = "Asia/Kolkata",
        string CalendarVersion = BillingCalendar.Version, string AccessPolicyVersion = GracePolicy.Version,
        Guid? PlanVersionId = null);

    internal static Task<Guid> SeedTermAsync(Hosts host, Guid account, TermSeed seed) =>
        SeedTermAsync(host.LicensingOwner, account, seed);

    internal static async Task<Guid> SeedTermAsync(NpgsqlConnectionStringBuilder owner, Guid account, TermSeed seed)
    {
        await using var connection = new NpgsqlConnection(owner.ConnectionString);
        await connection.OpenAsync();
        var term = Guid.NewGuid();
        await using (var coverage = new NpgsqlCommand("""
            INSERT INTO public.subscription_coverage_terms (term_id, account_id, mode, capacity, starts_at, ends_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            """, connection))
        {
            coverage.Parameters.AddWithValue(term);
            coverage.Parameters.AddWithValue(account);
            coverage.Parameters.AddWithValue(seed.Grouped ? "Grouped" : "Independent");
            coverage.Parameters.AddWithValue(seed.Grouped ? seed.Capacity : 1);
            coverage.Parameters.AddWithValue(seed.StartsAt);
            coverage.Parameters.AddWithValue(seed.EndsAt);
            await coverage.ExecuteNonQueryAsync();
        }

        if (seed.Basis is not null)
            await InsertFactsAsync(connection, account, term, seed);
        return term;
    }

    internal static async Task InsertFactsAsync(NpgsqlConnection connection, Guid account, Guid term, TermSeed seed)
    {
        await using var facts = new NpgsqlCommand("""
            INSERT INTO public.subscription_term_facts
                (term_id, account_id, basis, billing_period, anchor_local, time_zone, period_ordinal,
                 calendar_version, access_policy_version, member_allowance, price_paise, currency,
                 plan_version_id, source_reference, development_fixture, recorded_at)
            VALUES ($1, $2, $3, $4, $5::timestamp, $6, $7, $8, $9, $10, $11, $12, $13,
                'local-fixture-p0209', true, now())
            """, connection);
        facts.Parameters.AddWithValue(term);
        facts.Parameters.AddWithValue(account);
        facts.Parameters.AddWithValue(seed.Basis!);
        facts.Parameters.AddWithValue(seed.Period);
        facts.Parameters.AddWithValue(seed.AnchorLocal);
        facts.Parameters.AddWithValue((object?)seed.Zone ?? DBNull.Value);
        facts.Parameters.AddWithValue(seed.Ordinal);
        facts.Parameters.AddWithValue(seed.CalendarVersion);
        facts.Parameters.AddWithValue(seed.AccessPolicyVersion);
        facts.Parameters.AddWithValue(seed.MemberAllowance);
        facts.Parameters.AddWithValue((object?)seed.PricePaise ?? DBNull.Value);
        facts.Parameters.AddWithValue(seed.PricePaise is null ? DBNull.Value : "INR");
        facts.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Uuid, (object?)seed.PlanVersionId ?? DBNull.Value);
        await facts.ExecuteNonQueryAsync();
    }

    /// <summary>The applied affiliation the P02-05 consent flow would have written, seeded at the term start.</summary>
    internal static Task AffiliateAsync(Hosts host, Guid account, Guid payer, Guid shop, Guid owner, Guid? term,
        DateTimeOffset appliedAt, DateTimeOffset? endsAt) =>
        AffiliateAsync(host.LicensingOwner, account, payer, shop, owner, term, appliedAt, endsAt);

    internal static async Task AffiliateAsync(NpgsqlConnectionStringBuilder settings, Guid account, Guid payer, Guid shop,
        Guid owner, Guid? term, DateTimeOffset appliedAt, DateTimeOffset? endsAt, string shopName = "Status Fixture Shop")
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        var request = Guid.NewGuid();
        await using var sql = new NpgsqlBatch(connection);
        sql.BatchCommands.Add(new NpgsqlBatchCommand("""
            INSERT INTO public.shop_affiliation_requests
                (request_id, account_id, account_name, shop_id, requester_id, requester_revision, operation_key,
                 term_id, created_at, expires_at)
            VALUES ($1, $2, 'Status Fixture Payer', $3, $4, 1, gen_random_uuid(), $5, $6, $6 + interval '7 days')
            """) { Parameters = { new() { Value = request }, new() { Value = account }, new() { Value = shop },
                new() { Value = payer }, new() { Value = (object?)term ?? DBNull.Value }, new() { Value = appliedAt } } });
        sql.BatchCommands.Add(new NpgsqlBatchCommand("""
            INSERT INTO public.shop_affiliations
                (request_id, account_id, shop_id, term_id, event_id, actor_id, membership_id, authority_version,
                 shop_name, owner_name, accepted_at, applied_at, coverage_ends_at)
            VALUES ($1, $2, $3, $4, gen_random_uuid(), $5, gen_random_uuid(), 1, $8,
                'Fixture Owner', $6, $6, $7)
            """) { Parameters = { new() { Value = request }, new() { Value = account }, new() { Value = shop },
                new() { Value = (object?)term ?? DBNull.Value }, new() { Value = owner }, new() { Value = appliedAt }, new() { Value = (object?)endsAt ?? DBNull.Value }, new() { Value = shopName } } });
        await sql.ExecuteNonQueryAsync();
    }

    /// <summary>A billing account and its Owner row seeded directly, for provider-only hosts without a merchant API.</summary>
    internal static async Task<(Guid Account, Guid Owner)> SeedAccountAsync(NpgsqlConnectionStringBuilder settings)
    {
        var account = Guid.NewGuid();
        var owner = Guid.NewGuid();
        await LicensingPostgreSqlFixture.ExecuteAsync(settings, $"""
            INSERT INTO billing_accounts(id, name, created_by_merchant_user_id, creation_key, creation_request_hash, created_at)
                VALUES ('{account}', 'Status Fixture Payer', '{owner}', gen_random_uuid(), decode(repeat('00', 32), 'hex'), now());
            INSERT INTO billing_account_members(id, account_id, merchant_user_id, role, status, revision, created_at)
                VALUES (gen_random_uuid(), '{account}', '{owner}', 1, 1, 1, now());
            """);
        return (account, owner);
    }

    internal static Task<HttpResponseMessage> ShopStatusAsync(HttpClient client, Guid shop) =>
        client.GetAsync(new Uri($"/api/licensing/shops/{shop:D}/status", UriKind.Relative));

    internal static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    internal static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
