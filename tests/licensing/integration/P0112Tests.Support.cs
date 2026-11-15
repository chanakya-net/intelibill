using System.Net;
using System.Text.Json;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0108Tests
{
    /// <summary>Bootstraps and enrolls the first owner, keeping the one-time recovery codes.</summary>
    internal static async Task<(string Password, string Seed, string[] Codes)> EnrollOwnerWithCodesAsync(
        LicensingApiFactory api, P0106Tests.TestClock clock)
    {
        using var enrollment = Client(api);
        var password = "Fixture-" + Guid.NewGuid().ToString("N");
        await BootstrapAsync(enrollment, api, password);
        var csrf = await ReadMfaCsrfAsync(enrollment);
        var seed = await SetupSecretAsync(enrollment, csrf);
        using var response = await VerifyAsync(enrollment, csrf, AuthenticatorCode(seed, clock.GetUtcNow()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var codes = body.RootElement.GetProperty("recoveryCodes").EnumerateArray().Select(code => code.GetString()!).ToArray();
        clock.Advance(TimeSpan.FromSeconds(30));
        return (password, seed, codes);
    }
}

public sealed partial class P0112Tests
{
    internal const string OwnerEmail = "p0108.owner@example.test";
    private const string RecoveryCode = "/api/admin/auth/recovery/code";
    private const string RecoveryReset = "/api/admin/auth/recovery/reset";
    private const string Resets = "/api/admin/recovery/resets";
    private const string Session = "/api/admin/auth/session";
    private const string Refresh = "/api/admin/auth/refresh";
    private const string Business = "/api/admin/access";
    private const string Mfa = "/api/admin/auth/bootstrap/enrollment/mfa";
    private const string Resume = "/api/admin/auth/bootstrap/enrollment/resume";

    private async Task<Fixture> StartAsync()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = P0108Tests.SignInMfaSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var mailer = new P0114Tests.FixtureMailer();
        var api = new LicensingApiFactory(runtime, clock: clock, settings: settings,
            configure: services => services.AddSingleton<IAdminInvitationMailer>(mailer));
        var (password, seed, codes) = await P0108Tests.EnrollOwnerWithCodesAsync(api, clock);
        return new(runtime, clock, api, mailer, password, seed, codes);
    }

    internal sealed record Fixture(NpgsqlConnectionStringBuilder Runtime, P0106Tests.TestClock Clock,
        LicensingApiFactory Api, P0114Tests.FixtureMailer Mailer, string Password, string Seed, string[] Codes)
        : IAsyncDisposable
    {
        internal P0110CookieBrowser Browser() => new(P0114Tests.RawClient(Api));

        internal async Task<P0110CookieBrowser> OwnerAsync(string? seed = null)
        {
            Clock.Advance(TimeSpan.FromSeconds(30));
            return await P0114Tests.SignInAsync(Api, Clock, OwnerEmail, Password, seed ?? Seed);
        }

        internal async Task<string> InviteeSeedAsync(P0110CookieBrowser owner)
        {
            await P0114Tests.IssueAsync(owner, P0114Tests.Invitee);
            using var enrollment = P0114Tests.EnrollmentClient(Api);
            using var accepted = await P0114Tests.AcceptAsync(enrollment, Mailer.Token);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            return await P0114Tests.CompleteEnrollmentAsync(enrollment, Clock);
        }

        internal async Task<HttpStatusCode> SignInStatusAsync(string email, string password, string seed)
        {
            Clock.Advance(TimeSpan.FromSeconds(30));
            using var browser = Browser();
            using var response = await PostAsync(browser, "/api/admin/auth/sign-in",
                new { email, password, otp = P0108Tests.SignInOtp(seed, Clock.GetUtcNow()) });
            return response.StatusCode;
        }

        internal Task<object?> ScalarAsync(string sql) => LicensingPostgreSqlFixture.ReadScalarAsync(Runtime, sql);

        public ValueTask DisposeAsync() => Api.DisposeAsync();
    }

    /// <summary>Posts with the provider sign-in CSRF proof and the exact provider origin.</summary>
    private static async Task<HttpResponseMessage> PostAsync(P0110CookieBrowser browser, string path, object body,
        string origin = P0110CookieBrowser.Origin)
    {
        using var context = await browser.SendAsync(HttpMethod.Get, "/api/admin/auth/sign-in/context");
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        using var json = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        var response = await browser.SendAsync(HttpMethod.Post, path, body,
            json.RootElement.GetProperty("requestToken").GetString(), origin);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return response;
    }

    /// <summary>Completes authenticator enrollment with the enrollment cookie this browser holds.</summary>
    private static async Task<(string Seed, string[] Codes)> EnrollAsync(P0110CookieBrowser browser, P0106Tests.TestClock clock)
    {
        using var context = await browser.SendAsync(HttpMethod.Get, Mfa);
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        using var csrf = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        var token = csrf.RootElement.GetProperty("requestToken").GetString();
        using var started = await browser.SendAsync(HttpMethod.Post, Mfa + "/start", new { }, token, csrfHeader: "X-Bootstrap-CSRF");
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var setup = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var seed = setup.RootElement.GetProperty("setupSecret").GetString()!;
        using var verified = await browser.SendAsync(HttpMethod.Post, Mfa + "/verify",
            new { otp = P0108Tests.SignInOtp(seed, clock.GetUtcNow()) }, token, csrfHeader: "X-Bootstrap-CSRF");
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var body = JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
        clock.Advance(TimeSpan.FromSeconds(30));
        return (seed, body.RootElement.GetProperty("recoveryCodes").EnumerateArray().Select(code => code.GetString()!).ToArray());
    }

    private static async Task<HttpStatusCode> StatusAsync(P0110CookieBrowser browser, HttpMethod method, string path)
    {
        if (method == HttpMethod.Get)
        {
            using var read = await browser.SendAsync(method, path);
            return read.StatusCode;
        }

        using var write = await PostAsync(browser, path, new { });
        return write.StatusCode;
    }

    private static async Task AssertEndedAsync(P0110CookieBrowser browser)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(browser, HttpMethod.Get, Session));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(browser, HttpMethod.Post, Refresh));
    }

    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }

    private static void AssertNoSecrets(string text, params string[] secrets)
    {
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret.Replace("-", "", StringComparison.Ordinal), text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
