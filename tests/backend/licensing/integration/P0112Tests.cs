using System.Net;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P01-12: recovery never bypasses MFA. Recovery proof is one-use, every recovery ends the
/// administrator's sessions and only a fresh authenticator enrollment restores business access.
/// </summary>
public sealed partial class P0112Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task RecoveryCodeEndsSessionsAndRequiresEnrollmentBeforeBusinessAccess()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.OwnerAsync();
        using var saved = owner.Fork(P0114Tests.RawClient(fixture.Api));
        using var recovering = fixture.Browser();

        using var wrongPassword = await PostAsync(recovering, RecoveryCode,
            new { email = OwnerEmail, password = "Wrong-password-0000", recoveryCode = fixture.Codes[0] });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal("Admin.RecoveryRefused", await CodeOfAsync(wrongPassword));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(owner, HttpMethod.Get, Session));

        using var recovered = await PostAsync(recovering, RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[0] });
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        using (var body = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync()))
            Assert.Equal("enrollmentRequired", body.RootElement.GetProperty("state").GetString());

        await AssertEndedAsync(owner);
        await AssertEndedAsync(saved);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(recovering, HttpMethod.Get, Business));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(recovering, HttpMethod.Get, Session));
        Assert.Equal(HttpStatusCode.Unauthorized, await fixture.SignInStatusAsync(OwnerEmail, fixture.Password, fixture.Seed));

        using var replay = await PostAsync(fixture.Browser(), RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[0] });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var resume = await ResumeAsync(fixture);
        Assert.Contains(resume.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM bootstrap_enrollment_credentials WHERE revoked_at IS NULL"));

        var (seed, codes) = await EnrollAsync(recovering, fixture.Clock);
        Assert.NotEqual(fixture.Seed, seed);
        Assert.Equal(10, codes.Length);
        using var renewed = await fixture.OwnerAsync(seed);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(renewed, HttpMethod.Get, Business));
        using var stale = await PostAsync(fixture.Browser(), RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[1] });
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);

        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recoveries WHERE kind = 1"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action = 21 AND reason = 26"));
        Assert.Equal(3L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action = 22 AND reason = 27"));
        await AssertAuditSafeAsync(fixture, renewed, "recovery-code-redeemed", fixture.Codes[0], fixture.Codes[1], fixture.Password);
    }

    [Fact]
    public async Task ConcurrentRedemptionsOfOneRecoveryCodeAdmitExactlyOne()
    {
        await using var fixture = await StartAsync();
        var request = new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[2] };
        var browsers = Enumerable.Range(0, 4).Select(_ => fixture.Browser()).ToArray();
        var contexts = await Task.WhenAll(browsers.Select(ContextAsync));
        var responses = await Task.WhenAll(browsers.Select((browser, index) => browser.SendAsync(HttpMethod.Post,
            RecoveryCode, request, contexts[index])));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            // A losing request can time out waiting for the owner fence before it can read the code.
            foreach (var response in responses.Where(response => response.StatusCode != HttpStatusCode.OK))
            {
                Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.ServiceUnavailable });
                Assert.Equal(response.StatusCode == HttpStatusCode.Unauthorized
                    ? "Admin.RecoveryRefused" : "Bootstrap.StorageUnavailable", await CodeOfAsync(response));
            }

            using var replayBrowser = fixture.Browser();
            using var replay = await PostAsync(replayBrowser, RecoveryCode, request);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            Assert.Equal("Admin.RecoveryRefused", await CodeOfAsync(replay));
            Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recoveries"));
            Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM bootstrap_enrollment_credentials WHERE revoked_at IS NULL"));
            Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recovery_codes WHERE consumed_at IS NOT NULL"));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
            foreach (var browser in browsers) browser.Dispose();
        }
    }

    [Fact]
    public async Task LastRecoverableOwnerStaysRecoverable()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.OwnerAsync();
        using var self = await PostAsync(owner, Resets, ReviewedReset(OwnerEmail));
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("Admin.LastRecoverableOwner", await CodeOfAsync(self));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(owner, HttpMethod.Get, Session));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recoveries"));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action = 25 AND reason = 30"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_identities WHERE mfa_enrolled_at IS NULL"));

        using var first = fixture.Browser();
        using var started = await PostAsync(first, RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[3] });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(first, HttpMethod.Get, Mfa));

        // The lapsed owner re-enters with another unused code rather than being locked out.
        using var again = fixture.Browser();
        using var reentered = await PostAsync(again, RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[4] });
        Assert.Equal(HttpStatusCode.OK, reentered.StatusCode);
        var (seed, _) = await EnrollAsync(again, fixture.Clock);
        using var renewed = await fixture.OwnerAsync(seed);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(renewed, HttpMethod.Get, Business));
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recoveries WHERE kind = 1"));
    }

    private static async Task<string?> ContextAsync(P0110CookieBrowser browser)
    {
        using var context = await browser.SendAsync(HttpMethod.Get, "/api/admin/auth/sign-in/context");
        using var json = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("requestToken").GetString();
    }

    private static async Task<HttpResponseMessage> ResumeAsync(Fixture fixture)
    {
        using var client = P0114Tests.EnrollmentClient(fixture.Api);
        using var context = await client.GetAsync(new Uri("/api/admin/auth/bootstrap/context", UriKind.Relative));
        using var json = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Resume, UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { email = OwnerEmail, password = fixture.Password }),
        };
        request.Headers.Add("Origin", P0110CookieBrowser.Origin);
        if (json.RootElement.TryGetProperty("requestToken", out var token) && token.ValueKind == JsonValueKind.String)
            request.Headers.Add("X-Bootstrap-CSRF", token.GetString());
        return await client.SendAsync(request);
    }

    private static async Task AssertAuditSafeAsync(Fixture fixture, P0110CookieBrowser reader, string action,
        params string[] secrets)
    {
        using var read = await reader.SendAsync(HttpMethod.Get, "/api/admin/audit?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var page = await read.Content.ReadAsStringAsync();
        Assert.Contains(action, page, StringComparison.Ordinal);
        AssertNoSecrets(page, secrets);
        var stored = (string)(await fixture.ScalarAsync(
            "SELECT string_agg(to_jsonb(e)::text, '') FROM provider_audit_events e"))!;
        AssertNoSecrets(stored, [.. secrets, OwnerEmail]);
    }
}
