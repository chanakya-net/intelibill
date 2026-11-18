using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0108Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Origin = "https://localhost";
    private const string Bootstrap = "/api/admin/auth/bootstrap";
    private const string Mfa = Bootstrap + "/enrollment/mfa";
    private const string Business = "/api/admin/access";
    private const string Invitations = "/api/admin/invitations/context";
    private const string Email = "p0108.owner@example.test";

    [Fact]
    public async Task PasswordProof_OpensAuthenticatorSetupButCannotEnterBusinessApis()
    {
        var runtime = await NewDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: MfaSettings());
        using var browser = Client(api);
        await AssertStatusAsync(browser, Business, HttpStatusCode.Unauthorized);
        await BootstrapAsync(browser, api);

        await AssertStatusAsync(browser, Business, HttpStatusCode.Forbidden);
        await AssertStatusAsync(browser, Invitations, HttpStatusCode.Unauthorized);
        var csrf = await ReadMfaCsrfAsync(browser);
        using var context = await StartAsync(browser, csrf);
        Assert.True(context.RootElement.GetProperty("setupSecret").GetString() is { Length: 32 });
    }

    private static Dictionary<string, string?> MfaSettings() => new()
    {
        ["AdminMfa:ActiveKeyId"] = "fixture",
        ["AdminMfa:Keys:fixture"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
    };

    private async Task<NpgsqlConnectionStringBuilder> NewDatabaseAsync()
    {
        var name = "p0108_" + Guid.NewGuid().ToString("N");
        await database.CreateMigratedLicensingDatabaseAsync(name);
        var migrator = database.Login(name, LicensingPostgreSqlFixture.MigratorRole);
        await LicensingPostgreSqlFixture.ExecuteAsync(migrator, """
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO licensing_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO licensing_runtime;
            GRANT EXECUTE ON FUNCTION public.claim_entitlement_delivery(timestamptz,uuid) TO licensing_runtime;
            GRANT EXECUTE ON FUNCTION public.finish_entitlement_delivery(uuid,uuid,timestamptz,boolean,boolean,text) TO licensing_runtime;
            GRANT EXECUTE ON FUNCTION public.due_entitlement_verification(timestamptz) TO licensing_runtime;
            """);
        return database.Login(name, LicensingPostgreSqlFixture.RuntimeRole);
    }

    private static HttpClient Client(LicensingApiFactory api, bool handleCookies = true) =>
        api.CreateClient(new() { BaseAddress = new Uri(Origin), HandleCookies = handleCookies });

    private static async Task<string> BootstrapAsync(HttpClient client, LicensingApiFactory api, string? password = null)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var proof = await scope.ServiceProvider.GetRequiredService<PrepareBootstrapAdminHandler>()
            .PrepareAsync(Email, "P01-08-fixture", Origin, false, CancellationToken.None);
        Assert.False(proof.IsError);
        using var context = await client.GetFromJsonAsync<JsonDocument>(Path(Bootstrap + "/context"));
        var csrf = context!.RootElement.GetProperty("requestToken").GetString()!;
        using var response = await PostAsync(client, Bootstrap, csrf,
            new { proofToken = proof.Value.Value, password = password ?? "Fixture-" + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return CookiePair(response, "Intelibill.Admin.BootstrapEnrollment");
    }

    private static async Task<string> ReadMfaCsrfAsync(HttpClient browser)
    {
        using var response = await browser.GetAsync(Path(Mfa));
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Pending enrollment should expose authenticator setup. HTTP {(int)response.StatusCode}.");
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<JsonDocument> StartAsync(HttpClient browser, string csrf)
    {
        using var response = await PostAsync(browser, Mfa + "/start", csrf, new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertStatusAsync(HttpClient client, string route, HttpStatusCode status)
    {
        using var response = await client.GetAsync(Path(route));
        Assert.Equal(status, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string route, string csrf, object body, string origin = Origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path(route)) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Bootstrap-CSRF", csrf);
        return await client.SendAsync(request);
    }

    private static Uri Path(string route) => new(route, UriKind.Relative);
}
