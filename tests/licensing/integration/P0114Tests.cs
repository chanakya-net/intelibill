using System.Net;
using System.Globalization;
using Npgsql;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0114Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Audit = "/api/admin/audit";

    [Fact]
    public async Task OwnerReadsBoundedUtcAuditHistory()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: Settings());
        using var browser = await SignInOwnerAsync(api, clock);
        using var response = await browser.SendAsync(HttpMethod.Get, Audit + "?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(payload.RootElement.GetProperty("items").EnumerateArray());
        Assert.NotNull(payload.RootElement.GetProperty("nextCursor").GetString());
        await CheckSeekAsync(runtime, browser);
    }

    [Fact]
    public async Task AuditMigrationMatchesTheModel()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(runtime.ConnectionString).UseSnakeCaseNamingConvention().Options;
        await using var context = new LicensingDbContext(options);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    private static async Task CheckSeekAsync(NpgsqlConnectionStringBuilder runtime, P0110CookieBrowser browser)
    {
        const string stamp = "2026-11-14 12:04:00.123456+00";
        await ExecuteAsync(runtime, $"""
            INSERT INTO provider_audit_events(id,action,occurred_at)
            SELECT ('00000000-0000-0000-0000-' || lpad(value::text,12,'0'))::uuid, 4, '{stamp}'
            FROM generate_series(1,102) value;
            INSERT INTO provider_audit_events(id,action,occurred_at) VALUES
                (gen_random_uuid(), 4, '2026-11-14 12:04:01Z'),
                (gen_random_uuid(), 4, '2026-11-14 12:04:00.123455Z');
            """);
        const string filter = Audit + "?action=proof-refused&fromUtc=2026-11-14T17:34:00.123456%2B05:30"
            + "&toUtc=2026-11-14T12:04:01Z&pageSize=100";
        using var first = await ReadAsync(browser, filter);
        var rows = first.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(100, rows.Length);
        Assert.Equal(Enumerable.Range(3, 100).Reverse().Select(id => TieId(id)), rows.Select(Id));
        Assert.All(rows, row => Assert.Equal(new DateTimeOffset(2026, 11, 14, 12, 4, 0, TimeSpan.Zero)
            .AddTicks(1234560), row.GetProperty("occurredAt").GetDateTimeOffset()));
        Assert.Equal(TimeSpan.Zero, rows[0].GetProperty("occurredAt").GetDateTimeOffset().Offset);
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("correlationId").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("before").ValueKind);

        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        // Another database connection appends after page one; its newer row belongs to a fresh traversal.
        await ExecuteAsync(runtime, """
            INSERT INTO provider_audit_events(id,action,occurred_at)
            VALUES(gen_random_uuid(), 4, '2026-11-14 12:04:00.5Z');
            """);
        using var second = await ReadAsync(browser, filter + "&cursor=" + cursor);
        Assert.Equal(new[] { TieId(2), TieId(1) },
            second.RootElement.GetProperty("items").EnumerateArray().Select(Id));
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextCursor").ValueKind);
        using var refreshed = await ReadAsync(browser, filter);
        Assert.NotEqual(TieId(102), Id(refreshed.RootElement.GetProperty("items")[0]));
        await AssertStatusAsync(browser, filter.Replace("proof-refused", "owner-created", StringComparison.Ordinal)
            + "&cursor=" + cursor, HttpStatusCode.BadRequest);
    }

    private static Guid TieId(int value) => Guid.Parse("00000000-0000-0000-0000-" +
        value.ToString("D12", CultureInfo.InvariantCulture));

    private static Guid Id(JsonElement row) => row.GetProperty("id").GetGuid();

    private static async Task<JsonDocument> ReadAsync(P0110CookieBrowser browser, string path)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertStatusAsync(P0110CookieBrowser browser, string path, HttpStatusCode expected)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, path);
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static Task ExecuteAsync(NpgsqlConnectionStringBuilder db, string sql) =>
        LicensingPostgreSqlFixture.ExecuteAsync(db, sql);

    private static Task<object?> ScalarAsync(NpgsqlConnectionStringBuilder db, string sql) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(db, sql);

    private static Dictionary<string, string?> Settings()
    {
        var settings = P0108Tests.SignInMfaSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        return settings;
    }

    private static async Task<P0110CookieBrowser> SignInOwnerAsync(LicensingApiFactory api, P0106Tests.TestClock clock)
    {
        var (enrollment, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        enrollment.Dispose();
        return await SignInAsync(api, clock, "p0108.owner@example.test", password, seed);
    }

    internal static HttpClient RawClient(LicensingApiFactory api) =>
        api.CreateClient(new() { BaseAddress = new Uri(P0110CookieBrowser.Origin), HandleCookies = false });

    internal static async Task<P0110CookieBrowser> SignInAsync(LicensingApiFactory api, P0106Tests.TestClock clock,
        string email, string password, string seed)
    {
        var browser = new P0110CookieBrowser(RawClient(api));
        const string signIn = "/api/admin/auth/sign-in";
        using var context = await browser.SendAsync(HttpMethod.Get, signIn + "/context");
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        using var body = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        using var response = await browser.SendAsync(HttpMethod.Post, signIn, new
        {
            email, password, otp = P0108Tests.SignInOtp(seed, clock.GetUtcNow()),
        }, body.RootElement.GetProperty("requestToken").GetString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return browser;
    }
}
