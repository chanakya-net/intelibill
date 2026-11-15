using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0108Tests
{
    [Fact]
    public async Task BoundAttempts_SurviveHostReplacementAndUnauthorisedRequestsSpendNothing()
    {
        var runtime = await NewDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var keys = MfaSettings();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: keys);
        using var browser = Client(api);
        var password = "Fixture-" + Guid.NewGuid().ToString("N");
        var cookie = await BootstrapAsync(browser, api, password);
        var csrf = await ReadMfaCsrfAsync(browser);
        var setupSecret = await SetupSecretAsync(browser, csrf);
        const string attempts = "SELECT attempts FROM admin_totp";
        await AssertRejectedContextAsync(browser, csrf);
        Assert.Equal(0, await ReadFactAsync(runtime, attempts));

        for (var i = 0; i < 5; i++)
        {
            using var invalid = await VerifyAsync(browser, csrf, null);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        await using var replica = new LicensingApiFactory(runtime, clock: clock, settings: keys);
        using var otherTab = Client(replica, handleCookies: false);
        var replicaCsrf = await BindEnrollmentAsync(otherTab, cookie);
        var otp = AuthenticatorCode(setupSecret, clock.GetUtcNow());
        using var throttled = await VerifyAsync(otherTab, replicaCsrf, otp);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(15), throttled.Headers.RetryAfter?.Delta);
        Assert.Equal(5, await ReadFactAsync(runtime, attempts));
        Assert.Equal(5L, await ReadFactAsync(runtime, "SELECT count(*) FROM provider_audit_events WHERE action = 13"));
        Assert.Equal(0L, await ReadFactAsync(runtime, "SELECT count(*) FROM admin_recovery_codes"));

        await AssertBudgetSurvivesResumeAsync(browser, replica, clock, password, setupSecret);
    }

    private static async Task AssertBudgetSurvivesResumeAsync(HttpClient oldBrowser, LicensingApiFactory api,
        P0106Tests.TestClock clock, string password, string setupSecret)
    {
        clock.Advance(TimeSpan.FromMinutes(14));
        using var resumed = Client(api);
        using var resumeContext = await resumed.GetFromJsonAsync<JsonDocument>(Path(Bootstrap + "/context"));
        using var resume = await PostAsync(resumed, Bootstrap + "/enrollment/resume",
            resumeContext!.RootElement.GetProperty("requestToken").GetString()!, new { email = Email, password });
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        await AssertStatusAsync(oldBrowser, Mfa, HttpStatusCode.Unauthorized);
        var resumeCsrf = await ReadMfaCsrfAsync(resumed);
        using var stillThrottled = await VerifyAsync(resumed, resumeCsrf, AuthenticatorCode(setupSecret, clock.GetUtcNow()));
        Assert.Equal(HttpStatusCode.TooManyRequests, stillThrottled.StatusCode);
        clock.Advance(TimeSpan.FromMinutes(1));
        using var afterReset = await VerifyAsync(resumed, resumeCsrf, AuthenticatorCode(setupSecret, clock.GetUtcNow()));
        Assert.Equal(HttpStatusCode.OK, afterReset.StatusCode);
    }

    [Fact]
    public async Task Setup_UsesItsOriginalKeyAfterRotationAndFailsClosedIfKeyIsMissing()
    {
        var runtime = await NewDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var initial = MfaSettings();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: initial);
        using var browser = Client(api);
        var cookie = await BootstrapAsync(browser, api);
        var seed = await SetupSecretAsync(browser, await ReadMfaCsrfAsync(browser));

        var rotated = MfaSettings();
        rotated["AdminMfa:Keys:next"] = rotated["AdminMfa:Keys:fixture"];
        rotated["AdminMfa:ActiveKeyId"] = "next";
        rotated.Remove("AdminMfa:Keys:fixture");
        await using var withoutOldKey = new LicensingApiFactory(runtime, clock: clock, settings: rotated);
        using var missingKeyBrowser = Client(withoutOldKey, handleCookies: false);
        var csrf = await BindEnrollmentAsync(missingKeyBrowser, cookie);
        var otp = AuthenticatorCode(seed, clock.GetUtcNow());
        using var refused = await PostAsync(missingKeyBrowser, Mfa + "/start", csrf, new { });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        using var unverified = await VerifyAsync(missingKeyBrowser, csrf, otp);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unverified.StatusCode);
        Assert.Equal(0L, await ReadFactAsync(runtime, "SELECT count(*) FROM admin_identities WHERE mfa_enrolled_at IS NOT NULL"));

        var retained = new Dictionary<string, string?>(rotated) { ["AdminMfa:Keys:fixture"] = initial["AdminMfa:Keys:fixture"] };
        await using var withOldKey = new LicensingApiFactory(runtime, clock: clock, settings: retained);
        using var rotatedBrowser = Client(withOldKey, handleCookies: false);
        csrf = await BindEnrollmentAsync(rotatedBrowser, cookie);
        var sameSecret = string.Equals(seed, await SetupSecretAsync(rotatedBrowser, csrf), StringComparison.Ordinal);
        Assert.True(sameSecret, "Setup must keep its original secret after rotation.");
        using var accepted = await VerifyAsync(rotatedBrowser, csrf, otp);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    private static async Task AssertRejectedContextAsync(HttpClient browser, string csrf)
    {
        using var wrongOrigin = await VerifyAsync(browser, csrf, "invalid", "https://unrelated.example.test");
        using var noCsrf = await VerifyAsync(browser, "", "invalid");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
    }

    private static async Task<string> BindEnrollmentAsync(HttpClient browser, string enrollment)
    {
        browser.DefaultRequestHeaders.Add("Cookie", enrollment);
        using var context = await browser.GetAsync(Path(Mfa));
        Assert.Equal(HttpStatusCode.OK, context.StatusCode);
        using var json = JsonDocument.Parse(await context.Content.ReadAsStringAsync());
        var csrfCookie = CookiePair(context, "Intelibill.Admin.BootstrapCsrf");
        browser.DefaultRequestHeaders.Remove("Cookie");
        browser.DefaultRequestHeaders.Add("Cookie", enrollment + "; " + csrfCookie);
        return json.RootElement.GetProperty("requestToken").GetString()!;
    }
}
