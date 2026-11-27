using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// One licensing host on its own migrated database: the bootstrap platform owner, an invited operator and
/// (when asked) a second operator. Grants go through the real capability route; sign-in uses password + TOTP.
/// </summary>
internal sealed class P0206Fixture : IAsyncDisposable
{
    internal const string Plans = "/api/admin/plans";
    internal const string Trial = "/api/admin/trial-policy/draft";
    internal const string Users = "/api/admin/users";
    internal const string Manager = "licensing-manager";
    internal const string FixtureToken = "p0206-fixture-only";

    private P0206Fixture(LicensingPostgreSqlFixture database, NpgsqlConnectionStringBuilder runtime,
        P0106Tests.TestClock clock, LicensingApiFactory api, P0114Tests.FixtureMailer mailer, string password,
        string seed)
    {
        Database = database;
        Runtime = runtime;
        Clock = clock;
        Api = api;
        Mailer = mailer;
        OwnerPassword = password;
        OwnerSeed = seed;
    }

    internal LicensingPostgreSqlFixture Database { get; }
    internal NpgsqlConnectionStringBuilder Runtime { get; }
    internal P0106Tests.TestClock Clock { get; }
    internal LicensingApiFactory Api { get; }
    internal P0114Tests.FixtureMailer Mailer { get; }
    private string OwnerPassword { get; }
    private string OwnerSeed { get; }
    internal (string Password, string Seed) OwnerCredentials => (OwnerPassword, OwnerSeed);
    internal NpgsqlConnectionStringBuilder Superuser => Database.Bootstrap(Runtime.Database);

    internal static async Task<P0206Fixture> StartAsync(LicensingPostgreSqlFixture database,
        IReadOnlyDictionary<string, string?>? overrides = null, Action<IServiceCollection>? configure = null,
        bool prepareProvider = true)
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = P0108Tests.SignInMfaSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        settings["AdminFixtures:AdminId"] = Guid.NewGuid().ToString();
        settings["AdminFixtures:Capabilities"] = Manager;
        settings["AdminFixtures:SessionDigest"] = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(FixtureToken)));
        if (overrides is not null)
            foreach (var (key, value) in overrides) settings[key] = value;
        var mailer = new P0114Tests.FixtureMailer();
        var api = new LicensingApiFactory(runtime, clock: clock, settings: settings,
            configure: services =>
            {
                services.AddSingleton<IAdminInvitationMailer>(mailer);
                configure?.Invoke(services);
            });
        var (password, seed, _) = prepareProvider ? await P0108Tests.EnrollOwnerWithCodesAsync(api, clock) : ("", "", []);
        return new(database, runtime, clock, api, mailer, password, seed);
    }

    internal Task<P0110CookieBrowser> SignInOwnerAsync()
    {
        Clock.Advance(TimeSpan.FromSeconds(30));
        return P0114Tests.SignInAsync(Api, Clock, P0112Tests.OwnerEmail, OwnerPassword, OwnerSeed);
    }

    /// <summary>Invite, accept and enroll the operator; returns its TOTP seed.</summary>
    internal async Task<string> EnrollOperatorAsync(P0110CookieBrowser owner, string email = P0114Tests.Invitee)
    {
        await P0114Tests.IssueAsync(owner, email);
        using var enrollment = P0114Tests.EnrollmentClient(Api);
        using var accepted = await PostEnrollmentAsync(enrollment, "/api/admin/auth/invitation/context",
            "/api/admin/auth/invitation/accept",
            new { invitationToken = Mailer.Token, email, password = P0114Tests.InviteePassword });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return await P0114Tests.CompleteEnrollmentAsync(enrollment, Clock);
    }

    internal Task<P0110CookieBrowser> SignInOperatorAsync(string seed, string email = P0114Tests.Invitee)
    {
        Clock.Advance(TimeSpan.FromSeconds(30));
        return P0114Tests.SignInAsync(Api, Clock, email, P0114Tests.InviteePassword, seed);
    }

    /// <summary>The owner (freshly signed in, so its MFA is recent) replaces the operator's grants.</summary>
    internal async Task GrantAsync(string email, params string[] capabilities)
    {
        using var owner = await SignInOwnerAsync();
        await GrantAsync(owner, email, capabilities);
    }

    internal static async Task GrantAsync(P0110CookieBrowser owner, string email, params string[] capabilities)
    {
        using var list = await owner.SendAsync(HttpMethod.Get, Users);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var row = body.RootElement.GetProperty("items").EnumerateArray().Single(item =>
            string.Equals(item.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase));
        using var saved = await owner.SendAsync(HttpMethod.Put, $"{Users}/{row.GetProperty("id").GetGuid()}/capabilities",
            new
            {
                capabilities, expectedVersion = row.GetProperty("version").GetUInt32(),
                reason = "responsibilities-changed", confirmed = true, disable = false,
            }, await ProofAsync(owner));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    }

    /// <summary>An operator who holds only the Licensing Manager grant, signed in after the grant.</summary>
    internal async Task<(P0110CookieBrowser Manager, string Seed)> ManagerAsync(string email = P0114Tests.Invitee)
    {
        using (var owner = await SignInOwnerAsync())
        {
            var seed = await EnrollOperatorAsync(owner, email);
            await GrantAsync(email, Manager);
            return (await SignInOperatorAsync(seed, email), seed);
        }
    }

    internal static async Task<string?> ProofAsync(P0110CookieBrowser browser)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, "/api/admin/auth/sign-in/context");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("requestToken").GetString();
    }

    internal static async Task<HttpResponseMessage> PutPlanAsync(P0110CookieBrowser browser, Guid planId, object body,
        string origin = P0110CookieBrowser.Origin) =>
        await browser.SendAsync(HttpMethod.Put, $"{Plans}/{planId}/draft", body, await ProofAsync(browser), origin);

    internal static async Task<HttpResponseMessage> PutRawAsync(P0110CookieBrowser browser, string path, string json)
    {
        var proof = await ProofAsync(browser);
        return await browser.SendAsync(HttpMethod.Put, path,
            new StringContent(json, Encoding.UTF8, "application/json"), proof);
    }

    internal static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    internal static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("code").GetString();

    internal Task<object?> ScalarAsync(string sql) => LicensingPostgreSqlFixture.ReadScalarAsync(Superuser, sql);

    internal Task ExecuteAsync(string sql) => LicensingPostgreSqlFixture.ExecuteAsync(Superuser, sql);

    private static async Task<HttpResponseMessage> PostEnrollmentAsync(HttpClient client, string context, string path,
        object body)
    {
        using var source = await client.GetFromJsonAsync<JsonDocument>(new Uri(context, UriKind.Relative));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Origin", P0110CookieBrowser.Origin);
        request.Headers.Add("X-Bootstrap-CSRF", source!.RootElement.GetProperty("requestToken").GetString());
        return await client.SendAsync(request);
    }

    public ValueTask DisposeAsync() => Api.DisposeAsync();
}
