using System.Net;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0110Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string SignIn = "/api/admin/auth/sign-in";
    private const string Refresh = "/api/admin/auth/refresh";
    private const string Session = "/api/admin/auth/session";
    private const string Invitations = "/api/admin/invitations/context";
    private const string RefreshCookie = "__Host-InteliBill.Admin.Refresh";
    private const int ReuseRevokedAction = 16;

    [Fact]
    public async Task RefreshRotatesTheCredentialWithinOneSessionAndStoresOnlyItsHash()
    {
        await using var signed = await SignInAsync();
        var root = signed.Browser[RefreshCookie];
        Assert.NotNull(root);
        signed.Clock.Advance(TimeSpan.FromMinutes(14));

        using var refreshed = await RefreshAsync(signed.Browser, signed.Csrf);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.True(refreshed.Headers.CacheControl?.NoStore);
        Assert.Contains(refreshed.Headers.GetValues("Set-Cookie"), header => header.StartsWith(RefreshCookie + "=", StringComparison.Ordinal)
            && header.Contains("path=/;", StringComparison.OrdinalIgnoreCase)
            && header.Contains("secure", StringComparison.OrdinalIgnoreCase)
            && header.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && header.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        using var body = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        Assert.Equal(signed.Clock.GetUtcNow() + TimeSpan.FromMinutes(15),
            body.RootElement.GetProperty("accessExpiresAt").GetDateTimeOffset());
        var successor = signed.Browser[RefreshCookie];
        Assert.NotNull(successor);
        Assert.NotEqual(root, successor);

        // The sign-in access token has expired; the renewed one carries the same session.
        signed.Clock.Advance(TimeSpan.FromMinutes(2));
        await AssertStatusAsync(signed.Browser, Session, HttpStatusCode.OK);
        Assert.Equal(1L, await CountAsync(signed, "admin_sessions"));
        Assert.Equal(2L, await CountAsync(signed, "admin_refresh_credentials"));
        Assert.Equal(1L, await CountAsync(signed, "admin_refresh_credentials WHERE consumed_at IS NOT NULL"));
        Assert.Equal(1L, await CountAsync(signed, $"admin_refresh_credentials WHERE token_hash = {Digest(successor)}"));
        Assert.Equal(0L, await CountAsync(signed,
            $"admin_refresh_credentials c WHERE strpos(row_to_json(c)::text, '{root}') > 0 OR strpos(row_to_json(c)::text, '{successor}') > 0"));
    }

    [Fact]
    public async Task ReplayingAConsumedCredentialEndsTheSessionForEveryHolder()
    {
        await using var signed = await SignInAsync();
        using var stolen = signed.Browser.Fork(RawClient(signed.Api));
        using var rotated = await RefreshAsync(signed.Browser, signed.Csrf);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        using var replay = await RefreshAsync(stolen, signed.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Null(stolen[RefreshCookie]);
        await AssertStatusAsync(signed.Browser, Session, HttpStatusCode.Unauthorized);
        using var successor = await RefreshAsync(signed.Browser, signed.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);

        Assert.Equal(1L, await CountAsync(signed, "admin_sessions WHERE revoked_at IS NOT NULL"));
        Assert.Equal(2L, await CountAsync(signed, "admin_refresh_credentials"));
        Assert.Equal(1L, await CountAsync(signed, $"provider_audit_events WHERE action = {ReuseRevokedAction}"));
    }

    private async Task<SignedIn> SignInAsync(Dictionary<string, string?>? settings = null)
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        settings ??= Settings();
        var api = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        var (enrollment, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        enrollment.Dispose();
        var browser = new P0110CookieBrowser(RawClient(api));
        var csrf = await ReadCsrfAsync(browser);
        using var response = await browser.SendAsync(HttpMethod.Post, SignIn, new
        {
            email = "p0108.owner@example.test", password, otp = P0108Tests.SignInOtp(seed, clock.GetUtcNow()),
        }, csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new SignedIn(runtime, clock, settings, api, browser, csrf);
    }

    private static Dictionary<string, string?> Settings()
    {
        var settings = P0108Tests.SignInMfaSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        return settings;
    }

    private static HttpClient RawClient(LicensingApiFactory api) =>
        api.CreateClient(new() { BaseAddress = new Uri(P0110CookieBrowser.Origin), HandleCookies = false });

    private static async Task<string> ReadCsrfAsync(P0110CookieBrowser browser)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, SignIn + "/context");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(P0110CookieBrowser browser, string? csrf,
        string origin = P0110CookieBrowser.Origin) => browser.SendAsync(HttpMethod.Post, Refresh, csrf: csrf, origin: origin);

    private static async Task AssertStatusAsync(P0110CookieBrowser browser, string path, HttpStatusCode status)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, path);
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static Task<object?> CountAsync(SignedIn signed, string from) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(signed.Runtime, "SELECT count(*) FROM " + from);

    private static string Digest(string raw) => "'" + Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))) + "'";

    private sealed record SignedIn(NpgsqlConnectionStringBuilder Runtime, P0106Tests.TestClock Clock,
        Dictionary<string, string?> Settings, LicensingApiFactory Api, P0110CookieBrowser Browser, string Csrf) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Browser.Dispose();
            await Api.DisposeAsync();
        }
    }
}
