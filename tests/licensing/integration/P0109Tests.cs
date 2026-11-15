using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0108Tests
{
    internal static async Task<(HttpClient Browser, string Password, string Seed)> EnrollSignInOwnerAsync(
        LicensingApiFactory api, P0106Tests.TestClock clock)
    {
        using var enrollment = Client(api);
        var password = "Fixture-" + Guid.NewGuid().ToString("N");
        await BootstrapAsync(enrollment, api, password);
        var csrf = await ReadMfaCsrfAsync(enrollment);
        var seed = await SetupSecretAsync(enrollment, csrf);
        using var response = await VerifyAsync(enrollment, csrf, AuthenticatorCode(seed, clock.GetUtcNow()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        clock.Advance(TimeSpan.FromSeconds(30));
        return (Client(api), password, seed);
    }

    internal static string SignInOtp(string seed, DateTimeOffset now) => AuthenticatorCode(seed, now);
    internal Task<Npgsql.NpgsqlConnectionStringBuilder> SignInDatabaseAsync() => NewDatabaseAsync();
    internal static Dictionary<string, string?> SignInMfaSettings() => MfaSettings();
}

public sealed partial class P0109Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string SignIn = "/api/admin/auth/sign-in";
    private const string Session = "/api/admin/auth/session";
    private const string Origin = "https://localhost";

    [Fact]
    public async Task EnrolledPasswordAndTotp_OpenAnOwnerSessionWithOnlyGrantedCapabilities()
    {
        var enrollment = new P0108Tests(database);
        var runtime = await enrollment.SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: Settings());
        var (browser, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using (browser)
        {
            var csrf = await ReadCsrfAsync(browser);
            using var signedIn = await PostAsync(browser, password, P0108Tests.SignInOtp(seed, clock.GetUtcNow()), csrf);
            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
            Assert.Contains(signedIn.Headers.GetValues("Set-Cookie"),
                header => header.Contains("httponly", StringComparison.OrdinalIgnoreCase)
                    && header.Contains("secure", StringComparison.OrdinalIgnoreCase)
                    && header.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));

            using var session = await browser.GetFromJsonAsync<JsonDocument>(new Uri(Session, UriKind.Relative));
            var capabilities = session!.RootElement.GetProperty("capabilities").EnumerateArray()
                .Select(capability => capability.GetString()).ToArray();
            Assert.Contains("admin.invitations.manage", capabilities);
            Assert.DoesNotContain(capabilities, capability => capability!.StartsWith("billing.", StringComparison.Ordinal)
                || capability.StartsWith("security.", StringComparison.Ordinal));
        }
    }

    private static Dictionary<string, string?> Settings()
    {
        var settings = P0108Tests.SignInMfaSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        return settings;
    }

    private static async Task<string> ReadCsrfAsync(HttpClient browser)
    {
        using var response = await browser.GetAsync(new Uri(SignIn + "/context", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient browser, string password, string otp, string? csrf,
        string origin = Origin, string email = "p0108.owner@example.test")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(SignIn, UriKind.Relative))
        {
            Content = JsonContent.Create(new { email, password, otp }),
        };
        request.Headers.Add("Origin", origin);
        if (csrf is not null) request.Headers.Add("X-Admin-CSRF", csrf);
        return await browser.SendAsync(request);
    }
}
