using System.Net;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0106Tests
{
    private const string EnrollmentPath = BootstrapPath + "/enrollment";
    private const string EnrollmentCookieName = "Intelibill.Admin.BootstrapEnrollment";
    private const string BusinessPath = "/api/admin/access";

    [Fact]
    public async Task BootstrapCredential_OnlyAuthorizesPendingInitialOwnerEnrollment()
    {
        var runtime = await NewFixtureAsync();
        var clock = new TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock);
        using var browser = Client(api);
        var proof = await PrepareAsync(runtime, clock);
        var password = NewPassword();
        using var setup = await SubmitAsync(browser, await ReadCsrfAsync(browser), proof, password);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var cookie = ReadEnrollmentCookie(setup);
        using (var enrollment = await browser.GetAsync(new Uri(EnrollmentPath, UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, enrollment.StatusCode);
            using var document = JsonDocument.Parse(await enrollment.Content.ReadAsStringAsync());
            Assert.Equal("enrollmentRequired", document.RootElement.GetProperty("state").GetString());
            Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        }

        await using var restarted = new LicensingApiFactory(runtime, clock: clock);
        using var newBrowser = Client(restarted);
        await AssertCookieStatusAsync(newBrowser, EnrollmentPath, cookie.Pair, HttpStatusCode.OK);
        await AssertCookieStatusAsync(newBrowser, BusinessPath, cookie.Pair, HttpStatusCode.Forbidden);
        await AssertCookieStatusAsync(newBrowser, EnrollmentPath, EnrollmentCookieName + "=" + proof, HttpStatusCode.Unauthorized);
        await AssertBearerDeniedAsync(newBrowser, cookie.Pair);

        clock.Advance(TimeSpan.FromMinutes(15));
        await AssertCookieStatusAsync(newBrowser, EnrollmentPath, cookie.Pair, HttpStatusCode.Unauthorized);
        using var refused = await PostAsync(newBrowser, EnrollmentPath + "/resume", await ReadCsrfAsync(newBrowser),
            new { email = Email, password = NewPassword() });
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        using var resumed = await PostAsync(newBrowser, EnrollmentPath + "/resume", await ReadCsrfAsync(newBrowser),
            new { email = Email, password });
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        var replacement = ReadEnrollmentCookie(resumed);
        Assert.True(!string.Equals(cookie.Pair, replacement.Pair, StringComparison.Ordinal));
        await AssertCookieStatusAsync(newBrowser, EnrollmentPath, replacement.Pair, HttpStatusCode.OK);
        await AssertCookieStatusAsync(newBrowser, BusinessPath, replacement.Pair, HttpStatusCode.Forbidden);
        await AssertOwnerFactsAsync(runtime, proof, 1);
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM bootstrap_enrollment_credentials WHERE revoked_at IS NOT NULL"));
        await AssertPendingStateIsLiveAsync(runtime, restarted, replacement.Pair);
    }

    private async Task AssertPendingStateIsLiveAsync(
        Npgsql.NpgsqlConnectionStringBuilder runtime, LicensingApiFactory api, string pair)
    {
        // Revoke the credential while the owner is still pending. No MFA state is synthesized.
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Login(runtime.Database!, LicensingPostgreSqlFixture.MigratorRole),
            "UPDATE bootstrap_enrollment_credentials SET revoked_at = issued_at WHERE revoked_at IS NULL");
        using var independent = Client(api);
        await AssertCookieStatusAsync(independent, EnrollmentPath, pair, HttpStatusCode.Unauthorized);
    }

    private static (string Pair, string Attributes) ReadEnrollmentCookie(HttpResponseMessage response)
    {
        var value = response.Headers.GetValues("Set-Cookie").Single(header =>
            header.StartsWith(EnrollmentCookieName + "=", StringComparison.Ordinal));
        var separator = value.IndexOf(';', StringComparison.Ordinal);
        return (value[..separator], value[separator..]);
    }

    private static async Task AssertCookieStatusAsync(HttpClient client, string path, string pair, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("Cookie", pair);
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    private static async Task AssertBearerDeniedAsync(HttpClient client, string pair)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(EnrollmentPath, UriKind.Relative));
        request.Headers.Add("Authorization", "Bearer " + pair[(EnrollmentCookieName.Length + 1)..]);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
