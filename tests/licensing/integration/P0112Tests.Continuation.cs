using System.Net;
using System.Security.Cryptography;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0112Tests
{
    private const string Continuation = "/api/admin/auth/recovery/continue";

    [Fact]
    public async Task FinalOwnerCodeRequiresSavedContinuationAndCanFinishAfterLostResponse()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.OwnerAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Runtime, """
            UPDATE admin_recovery_codes SET consumed_at = CURRENT_TIMESTAMP
            WHERE digest <> (SELECT min(digest) FROM admin_recovery_codes);
            """);
        var remaining = (string)(await fixture.ScalarAsync("SELECT digest FROM admin_recovery_codes WHERE consumed_at IS NULL"))!;
        var adminId = (Guid)(await fixture.ScalarAsync("SELECT id FROM admin_identities"))!;
        var code = fixture.Codes.Single(value => DigestFixtureCode(fixture, adminId, value) == remaining);
        var continuationKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        using var first = fixture.Browser();
        using var unconfirmed = await PostAsync(first, RecoveryCode, new { email = OwnerEmail, password = fixture.Password, recoveryCode = code });
        Assert.Equal(HttpStatusCode.Conflict, unconfirmed.StatusCode);
        Assert.Equal("Admin.RecoveryContinuationRequired", await CodeOfAsync(unconfirmed));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(owner, HttpMethod.Get, Session));

        var request = new { email = OwnerEmail, password = fixture.Password, recoveryCode = code, continuationKey };
        using var second = fixture.Browser();
        using var third = fixture.Browser();
        var responses = await Task.WhenAll(PostAsync(second, RecoveryCode, request), PostAsync(third, RecoveryCode, request));
        using var accepted = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        using var denied = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        await AssertEndedAsync(owner);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await AssertContinuationCompletesAsync(fixture, code, continuationKey);
    }

    private static async Task AssertContinuationCompletesAsync(Fixture fixture, string code, string continuationKey)
    {
        using var browser = fixture.Browser();
        using var replay = await PostAsync(browser, RecoveryCode, new { email = OwnerEmail, password = fixture.Password, recoveryCode = code });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        var request = new { email = OwnerEmail, password = fixture.Password, continuationKey };
        using var resumed = await PostAsync(browser, Continuation, request);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(browser, HttpMethod.Get, Business));
        var (seed, _) = await EnrollAsync(browser, fixture.Clock);
        using var oldKey = await PostAsync(browser, Continuation, request);
        Assert.Equal(HttpStatusCode.Unauthorized, oldKey.StatusCode);
        using var renewed = await fixture.OwnerAsync(seed);
        Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(renewed, HttpMethod.Get, Business));
        await AssertAuditSafeAsync(fixture, renewed, "enrollment-resumed", continuationKey, code);
    }

    private static string DigestFixtureCode(Fixture fixture, Guid adminId, string code)
    {
        var root = Convert.FromBase64String(fixture.Settings["AdminMfa:Keys:fixture"]!);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, root, 32, info: "intelibill-admin/recovery-hmac/v1"u8.ToArray());
        var normalized = code.Replace("-", "", StringComparison.Ordinal);
        return Convert.ToHexString(HMACSHA256.HashData(key, System.Text.Encoding.ASCII.GetBytes($"{adminId:N}:{normalized}")));
    }

    [Fact]
    public async Task PendingSetupUnderRotatedKeyKeepsPriorCodesUntilVerification()
    {
        await using var fixture = await StartAsync();
        var settings = new Dictionary<string, string?>(fixture.Settings)
        {
            ["AdminMfa:ActiveKeyId"] = "next",
            ["AdminMfa:Keys:next"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        await using var api = new LicensingApiFactory(fixture.Runtime, clock: fixture.Clock, settings: settings);
        using var first = new P0110CookieBrowser(P0114Tests.RawClient(api));
        using var accepted = await PostAsync(first, RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[0] });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var (pendingSeed, _) = await StartEnrollmentAsync(first);
        Assert.NotEqual(fixture.Seed, pendingSeed);
        Assert.Equal("next", await fixture.ScalarAsync("SELECT key_id FROM admin_totp"));

        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        using var restarted = new P0110CookieBrowser(P0114Tests.RawClient(api));
        using var restartedResponse = await PostAsync(restarted, RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[1] });
        Assert.Equal(HttpStatusCode.OK, restartedResponse.StatusCode);
        await EnrollAsync(restarted, fixture.Clock);
        using var oldCode = await PostAsync(restarted, RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[2] });
        Assert.Equal(HttpStatusCode.Unauthorized, oldCode.StatusCode);
    }
}
