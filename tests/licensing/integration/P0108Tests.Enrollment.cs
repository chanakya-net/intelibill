using System.Net;
using System.Text.Json;
using Serilog.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0108Tests
{
    [Fact]
    public async Task Enrollment_RejectsInvalidProofAndReturnsRecoveryCodesOnlyToTheWinningRequest()
    {
        var runtime = await NewDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = MfaSettings();
        settings["Observability:Logging:MinimumLevel"] = "Verbose";
        var logs = new P0107Tests.CapturedLogs();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: settings,
            configure: services => services.AddSingleton<ILogEventSink>(logs));
        await using var replica = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        using var browser = Client(api);
        using var secondBrowser = Client(replica, handleCookies: false);
        await AssertStatusAsync(browser, Mfa, HttpStatusCode.Unauthorized);
        var cookie = await BootstrapAsync(browser, api);
        var csrf = await ReadMfaCsrfAsync(browser);
        var setupSecret = await SetupSecretAsync(browser, csrf);
        var otp = AuthenticatorCode(setupSecret, clock.GetUtcNow());
        using (var invalid = await VerifyAsync(browser, csrf, "invalid"))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await AssertStatusAsync(browser, Business, HttpStatusCode.Forbidden);

        var secondCsrf = await BindEnrollmentAsync(secondBrowser, cookie);
        var recoveryCodes = await CompleteRaceAsync(browser, csrf, secondBrowser, secondCsrf, otp);
        await AssertStatusAsync(secondBrowser, Mfa, HttpStatusCode.Unauthorized);
        await AssertStatusAsync(browser, Business, HttpStatusCode.Unauthorized);
        await AssertStatusAsync(secondBrowser, Business, HttpStatusCode.Unauthorized);
        Assert.True(await ReadFactAsync(runtime, """
            SELECT (SELECT count(*) FROM admin_identities WHERE mfa_enrolled_at IS NOT NULL) = 1
               AND (SELECT count(*) FROM admin_recovery_codes WHERE consumed_at IS NULL AND length(digest) = 64) = 10
               AND (SELECT count(*) FROM bootstrap_enrollment_credentials WHERE revoked_at IS NOT NULL) = 1
               AND (SELECT count(*) FROM provider_audit_events WHERE action = 12) = 1
            """) is true);

        await AssertSeparatedKeysAsync(runtime, settings["AdminMfa:Keys:fixture"]!, setupSecret, recoveryCodes[0]);
        await AssertNoPlaintextAsync(runtime, [setupSecret, .. recoveryCodes], logs);
        Assert.True(!logs.Events.Any(entry => entry.Contains(otp, StringComparison.Ordinal)), "Diagnostic output contained an authenticator code.");
    }

    private static async Task<string[]> CompleteRaceAsync(HttpClient first, string firstCsrf,
        HttpClient second, string secondCsrf, string otp)
    {
        var responses = await Task.WhenAll(VerifyAsync(first, firstCsrf, otp), VerifyAsync(second, secondCsrf, otp));
        try
        {
            var winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            Assert.True(winner.Headers.CacheControl?.NoStore);
            Assert.False((await loser.Content.ReadAsStringAsync()).Contains("recoveryCodes", StringComparison.Ordinal));
            Assert.True(winner.Headers.GetValues("Set-Cookie").All(header => header.StartsWith(
                "Intelibill.Admin.BootstrapEnrollment=;", StringComparison.Ordinal)));
            using var result = JsonDocument.Parse(await winner.Content.ReadAsStringAsync());
            var codes = result.RootElement.GetProperty("recoveryCodes").EnumerateArray()
                .Select(code => code.GetString()!).ToArray();
            Assert.True(codes.Length == 10 && codes.Distinct(StringComparer.Ordinal).Count() == 10);
            return codes;
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient browser, string csrf, string? otp, string origin = Origin) =>
        PostAsync(browser, Mfa + "/verify", csrf, new { otp }, origin);
}
