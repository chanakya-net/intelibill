using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0109Tests
{
    [Fact]
    public async Task SignInRequiresMatchingOriginAndCsrfBeforeItCanConsumeMfa()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: Settings());
        var (browser, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using var owner = browser;
        var csrf = await ReadCsrfAsync(browser);
        var otp = P0108Tests.SignInOtp(seed, clock.GetUtcNow());
        using var wrongOrigin = await PostAsync(browser, password, otp, csrf, "https://other.example.test");
        using var missingProof = await PostAsync(browser, password, otp, null);
        using var malformedProof = await PostAsync(browser, password, otp, "not-a-valid-proof");
        using var second = api.CreateClient(new() { BaseAddress = new Uri(Origin) });
        await ReadCsrfAsync(second);
        using var foreignProof = await PostAsync(second, password, otp, csrf);
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missingProof.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, malformedProof.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignProof.StatusCode);
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime, "SELECT count(*) FROM admin_sign_in_buckets"));

        using var badPassword = await PostAsync(browser, "Wrong-fixture-password", otp, csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, badPassword.StatusCode);
        using var success = await PostAsync(browser, password, otp, csrf);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        await AssertStatusAsync(browser, "/api/admin/invitations/context", HttpStatusCode.OK);
        await AssertStatusAsync(browser, "/api/admin/access", HttpStatusCode.NoContent);
        clock.Advance(TimeSpan.FromMinutes(5));
        await AssertStatusAsync(browser, Session, HttpStatusCode.OK);
        await AssertStatusAsync(browser, "/api/admin/invitations/context", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevocationAndDeactivationAreCheckedAgainstPostgreSqlOnEveryRequest()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: Settings());
        var (browser, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using var owner = browser;
        var csrf = await ReadCsrfAsync(browser);
        using var login = await PostAsync(browser, password, P0108Tests.SignInOtp(seed, clock.GetUtcNow()), csrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        await LicensingPostgreSqlFixture.ExecuteAsync(runtime, "UPDATE admin_sessions SET revoked_at = mfa_verified_at");
        await AssertStatusAsync(browser, Session, HttpStatusCode.Unauthorized);
        clock.Advance(TimeSpan.FromSeconds(30));
        using var second = await PostAsync(browser, password, P0108Tests.SignInOtp(seed, clock.GetUtcNow()), csrf);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await LicensingPostgreSqlFixture.ExecuteAsync(runtime, "UPDATE admin_identities SET deactivated_at = mfa_enrolled_at");
        await AssertStatusAsync(browser, Session, HttpStatusCode.Unauthorized);
        await AssertStatusAsync(browser, "/api/admin/invitations/context", HttpStatusCode.Unauthorized);
        clock.Advance(TimeSpan.FromSeconds(30));
        using var inactive = await PostAsync(browser, password, P0108Tests.SignInOtp(seed, clock.GetUtcNow()), csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
    }

    [Fact]
    public async Task AccessExpiresAtFifteenMinutesAndNeitherMerchantNorOtherAdminKeyIsAccepted()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: Settings());
        await using var otherApi = new LicensingApiFactory(runtime, clock: clock, settings: Settings());
        var (browser, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using var owner = browser;
        var csrf = await ReadCsrfAsync(browser);
        using var login = await PostAsync(browser, password, P0108Tests.SignInOtp(seed, clock.GetUtcNow()), csrf);
        var cookie = AccessCookie(login);
        using var other = otherApi.CreateClient(new() { BaseAddress = new Uri(Origin), HandleCookies = false });
        other.DefaultRequestHeaders.Add("Cookie", cookie);
        await AssertStatusAsync(other, Session, HttpStatusCode.Unauthorized);
        using var unsigned = api.CreateClient(new() { BaseAddress = new Uri(Origin), HandleCookies = false });
        var jwt = CreateMerchantToken(clock.GetUtcNow());
        unsigned.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        await AssertStatusAsync(unsigned, Session, HttpStatusCode.Unauthorized);
        unsigned.DefaultRequestHeaders.Add("Cookie", "__Host-InteliBill.Admin.Access=" + jwt);
        await AssertStatusAsync(unsigned, Session, HttpStatusCode.Unauthorized);

        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        await AssertStatusAsync(browser, Session, HttpStatusCode.OK);
        clock.Advance(TimeSpan.FromSeconds(1));
        await AssertStatusAsync(browser, Session, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongIssuerOrAudienceFailsEvenWithTheFixtureSigningKeyAndLiveSession()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = Settings();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        var (browser, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using var owner = browser;
        using var login = await PostAsync(browser, password, P0108Tests.SignInOtp(seed, clock.GetUtcNow()), await ReadCsrfAsync(browser));
        var cookie = AccessCookie(login).Split('=', 2)[1];
        var token = new JwtSecurityTokenHandler().ReadJwtToken(cookie);
        var key = System.Security.Cryptography.HKDF.DeriveKey(System.Security.Cryptography.HashAlgorithmName.SHA256,
            Convert.FromBase64String(settings["AdminSessions:SigningKey"]!), 32,
            info: "intelibill-admin/access-signing/v1"u8.ToArray());
        try
        {
            await AssertWrongAuthorityAsync(api, token, key, "merchant.test", Assert.Single(token.Audiences));
            await AssertWrongAuthorityAsync(api, token, key, token.Issuer, "merchant.test");
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
    }

    private static async Task AssertWrongAuthorityAsync(LicensingApiFactory api, JwtSecurityToken valid, byte[] key,
        string issuer, string audience)
    {
        var wrong = new JwtSecurityToken(issuer, audience,
            valid.Claims.Where(claim => claim.Type is JwtRegisteredClaimNames.Sub or "sid"), valid.ValidFrom, valid.ValidTo,
            new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
        wrong.Header[JwtHeaderParameterNames.Typ] = valid.Header.Typ;
        var token = new JwtSecurityTokenHandler().WriteToken(wrong);
        using var browser = api.CreateClient(new() { BaseAddress = new Uri(Origin), HandleCookies = false });
        browser.DefaultRequestHeaders.Add("Cookie", "__Host-InteliBill.Admin.Access=" + token);
        await AssertStatusAsync(browser, Session, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistedIdleAndAbsoluteBoundariesAreEnforced(bool recentlySeen)
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: Settings());
        var (browser, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using var owner = browser;
        var csrf = await ReadCsrfAsync(browser);
        using var login = await PostAsync(browser, password, P0108Tests.SignInOtp(seed, clock.GetUtcNow()), csrf);
        var interval = recentlySeen ? "12 hours" : "30 minutes";
        var activity = recentlySeen ? "last_seen_at" : $"last_seen_at - interval '{interval}' + interval '1 second'";
        await LicensingPostgreSqlFixture.ExecuteAsync(runtime,
            $"UPDATE admin_sessions SET created_at = created_at - interval '{interval}' + interval '1 second', mfa_verified_at = mfa_verified_at - interval '{interval}' + interval '1 second', last_seen_at = {activity}");
        // At the pre-boundary instant the idle case must avoid a successful read that would restart its timer.
        if (recentlySeen) await AssertStatusAsync(browser, Session, HttpStatusCode.OK);
        clock.Advance(TimeSpan.FromSeconds(1));
        await AssertStatusAsync(browser, Session, HttpStatusCode.Unauthorized);
    }

    private static string CreateMerchantToken(DateTimeOffset now) => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken("merchant.test", "merchant.test",
            [new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()), new("active_shop_id", Guid.NewGuid().ToString()),
             new Claim("sid", Guid.NewGuid().ToString())], now.UtcDateTime, now.AddMinutes(15).UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('M', 32))),
                SecurityAlgorithms.HmacSha256)));

    private static string AccessCookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Single(header => header.StartsWith("__Host-InteliBill.Admin.Access=", StringComparison.Ordinal)).Split(';', 2)[0];

    private static async Task AssertStatusAsync(HttpClient client, string path, HttpStatusCode status)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
