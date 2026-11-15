using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0113Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Users = "/api/admin/users";
    private const string Session = "/api/admin/auth/session";
    private const string Refresh = "/api/admin/auth/refresh";
    private const string BootstrapEmail = P0112Tests.OwnerEmail;
    private const string OperatorEmail = P0114Tests.Invitee;
    private const string FixtureToken = "p0113-scope-fixture";
    private const string OwnerGrant = "platform-owner";

    private static readonly string[] SupportAndBilling = ["support", "billing"];

    [Fact]
    public async Task DelegationUsesLiveGrantsAndEndsOldSessions()
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.SignInOwnerAsync();
        var seed = await fixture.EnrollOperatorAsync(owner);
        using var previous = await fixture.SignInOperatorAsync(seed);
        var target = await FindAsync(owner, OperatorEmail);

        using var saved = await UpdateAsync(owner, target, ["support", "billing"]);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(SupportAndBilling, Grants(await ReadBodyAsync(saved)));
        await AssertEndedAsync(previous);

        using var member = await fixture.SignInOperatorAsync(seed);
        using var session = await member.SendAsync(HttpMethod.Get, Session);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal(SupportAndBilling, Grants(await ReadBodyAsync(session)));
        using var listing = await member.SendAsync(HttpMethod.Get, Users);
        Assert.Equal(HttpStatusCode.Forbidden, listing.StatusCode);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=105"));
    }

    [Fact]
    public async Task OwnerReductionAndRecoveryShareOneDatabaseFence()
    {
        await using var fixture = await StartAsync();
        using var first = await fixture.SignInOwnerAsync();
        var seed = await fixture.EnrollOperatorAsync(first);
        var candidate = await FindAsync(first, OperatorEmail);
        using var promoted = await UpdateAsync(first, candidate, [OwnerGrant]);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        using var second = await fixture.SignInOperatorAsync(seed);
        var firstRow = await FindAsync(second, BootstrapEmail);
        var secondRow = await FindAsync(second, OperatorEmail);

        var results = await RaceAsync(fixture, first, firstRow, second, secondRow);
        Assert.Single(results, result => result.Status == HttpStatusCode.OK);
        Assert.Single(results, result => result == (HttpStatusCode.Conflict, "Admin.LastRecoverableOwner"));
        Assert.Equal(1L, await fixture.ScalarAsync("""
            SELECT count(*) FROM admin_identities
            WHERE capabilities & 16 = 16 AND deactivated_at IS NULL AND mfa_enrolled_at IS NOT NULL
            """));
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action IN (105,106)"));
        using var surviving = await (results[0].Status == HttpStatusCode.OK ? second : first).SendAsync(HttpMethod.Get, Users);
        Assert.Equal(HttpStatusCode.OK, surviving.StatusCode);
        await AssertEndedAsync(results[0].Status == HttpStatusCode.OK ? first : second);
    }

    [Theory]
    [InlineData(299999, HttpStatusCode.OK, null)]
    [InlineData(300000, HttpStatusCode.Forbidden, "Admin.RecentMfaRequired")]
    [InlineData(300001, HttpStatusCode.Forbidden, "Admin.RecentMfaRequired")]
    public async Task CapabilityMutationRefusalsAreAtomic(int milliseconds, HttpStatusCode status, string? code)
    {
        await using var fixture = await StartAsync();
        using var owner = await fixture.SignInOwnerAsync();
        var seed = await fixture.EnrollOperatorAsync(owner);
        using var member = await fixture.SignInOperatorAsync(seed);
        var target = await FindAsync(owner, OperatorEmail);
        await AssertInvalidScopesAsync(fixture, owner, member, target);
        using var unconfirmed = await UpdateAsync(owner, target, ["billing"], confirmed: false);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        using var badGrant = await UpdateAsync(owner, target, ["owner"]);
        Assert.Equal(HttpStatusCode.BadRequest, badGrant.StatusCode);
        using var staleVersion = await UpdateAsync(owner, target with { Version = target.Version - 1 }, ["billing"]);
        Assert.Equal(HttpStatusCode.Conflict, staleVersion.StatusCode);
        using var noChange = await UpdateAsync(owner, target, []);
        Assert.Equal(HttpStatusCode.OK, noChange.StatusCode);
        Assert.Equal(target.Version, (await ReadBodyAsync(noChange)).GetProperty("version").GetUInt32());
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=105"));

        fixture.Clock.Advance(TimeSpan.FromMilliseconds(milliseconds) - (fixture.Clock.GetUtcNow() - fixture.OwnerProofAt));
        using var result = await UpdateAsync(owner, target, ["billing"]);
        Assert.Equal(status, result.StatusCode);
        if (code is not null) Assert.Equal(code, (await ReadBodyAsync(result)).GetProperty("code").GetString());
        Assert.Equal(status == HttpStatusCode.OK ? 1L : 0L,
            await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action=105"));
    }

    private static async Task AssertInvalidScopesAsync(Fixture fixture, P0110CookieBrowser owner,
        P0110CookieBrowser member, Target target)
    {
        using var insufficient = await UpdateAsync(member, target, ["billing"]);
        Assert.Equal(HttpStatusCode.Forbidden, insufficient.StatusCode);

        using var noCsrf = await SendUpdateAsync(owner, target, ["billing"], null);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var wrongOrigin = await SendUpdateAsync(owner, target, ["billing"], await ProofAsync(owner),
            origin: "https://other.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);

        using var fixtureSession = new P0110CookieBrowser(P0114Tests.RawClient(fixture.Api));
        fixtureSession.Set("Intelibill.Admin.FixtureSession", FixtureToken);
        using var wrongScheme = await UpdateAsync(fixtureSession, target, ["billing"]);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongScheme.StatusCode);

        using var merchant = P0114Tests.RawClient(fixture.Api);
        var merchantToken = P0109Tests.CreateMerchantToken(fixture.Clock.GetUtcNow());
        merchant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchantToken);
        merchant.DefaultRequestHeaders.Add("Cookie", "__Host-InteliBill.Admin.Access=" + merchantToken);
        using var wrongScope = await merchant.GetAsync(new Uri(Users, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongScope.StatusCode);
    }

    private async Task<(HttpStatusCode Status, string? Code)[]> RaceAsync(Fixture fixture,
        P0110CookieBrowser first, Target firstRow, P0110CookieBrowser second, Target secondRow)
    {
        var firstProof = await ProofAsync(first);
        var secondProof = await ProofAsync(second);
        await using var gate = new NpgsqlConnection(fixture.Runtime.ConnectionString);
        await gate.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await LockFenceAsync(gate, tx);
        var demote = SendUpdateAsync(first, firstRow, [], firstProof);
        var disable = SendUpdateAsync(second, secondRow, [OwnerGrant], secondProof, disable: true);
        try { await WaitForFenceAsync(fixture); }
        finally { await tx.RollbackAsync(); }
        return await Task.WhenAll(ReadOutcomeAsync(demote), ReadOutcomeAsync(disable));
    }

    private async Task WaitForFenceAsync(Fixture fixture, int expected = 2)
    {
        var monitor = database.Bootstrap(fixture.Runtime.Database);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!deadline.IsCancellationRequested)
        {
            var waiting = await LicensingPostgreSqlFixture.ReadScalarAsync(monitor, """
                SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND usename='licensing_runtime'
                    AND wait_event_type='Lock' AND query LIKE '%initial_admin_bootstrap%FOR UPDATE%'
                """);
            if ((long)waiting! >= expected) return;
            await Task.Delay(10);
        }
        Assert.Fail($"Expected {expected} requests waiting on the PostgreSQL owner fence.");
    }

    private async Task<Fixture> StartAsync()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = P0108Tests.SignInMfaSettings();
        settings["AdminSessions:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        settings["AdminFixtures:AdminId"] = Guid.NewGuid().ToString();
        settings["AdminFixtures:Capabilities"] = "admin.access.manage";
        settings["AdminFixtures:SessionDigest"] = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(FixtureToken)));
        var mailer = new P0114Tests.FixtureMailer();
        var api = new LicensingApiFactory(runtime, clock: clock, settings: settings,
            configure: services => services.AddSingleton<IAdminInvitationMailer>(mailer));
        var (password, seed, codes) = await P0108Tests.EnrollOwnerWithCodesAsync(api, clock);
        return new(runtime, clock, api, mailer, password, seed, codes);
    }

    private static async Task<Target> FindAsync(P0110CookieBrowser owner, string email)
    {
        using var response = await owner.SendAsync(HttpMethod.Get, Users);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = Assert.Single((await ReadBodyAsync(response)).GetProperty("items").EnumerateArray(),
            item => string.Equals(item.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase));
        return new(row.GetProperty("id").GetGuid(), row.GetProperty("version").GetUInt32());
    }

    private static async Task<HttpResponseMessage> UpdateAsync(P0110CookieBrowser browser, Target target,
        string[] capabilities, bool confirmed = true) =>
        await SendUpdateAsync(browser, target, capabilities, await ProofAsync(browser), confirmed: confirmed);

    private static Task<HttpResponseMessage> SendUpdateAsync(P0110CookieBrowser browser, Target target,
        string[] capabilities, string? csrf, bool disable = false, bool confirmed = true, string origin = P0110CookieBrowser.Origin) =>
        browser.SendAsync(HttpMethod.Put, $"{Users}/{target.Id}/capabilities",
            new { capabilities, expectedVersion = target.Version, reason = "responsibilities-changed", confirmed, disable }, csrf, origin);

    private static async Task<string?> ProofAsync(P0110CookieBrowser browser)
    {
        using var response = await browser.SendAsync(HttpMethod.Get, "/api/admin/auth/sign-in/context");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadBodyAsync(response)).GetProperty("requestToken").GetString();
    }

    private static async Task AssertEndedAsync(P0110CookieBrowser browser)
    {
        using var session = await browser.SendAsync(HttpMethod.Get, Session);
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
        using var refresh = await browser.SendAsync(HttpMethod.Post, Refresh, csrf: await ProofAsync(browser));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    private static async Task<(HttpStatusCode Status, string? Code)> ReadOutcomeAsync(Task<HttpResponseMessage> pending)
    {
        using var response = await pending;
        return (response.StatusCode, response.IsSuccessStatusCode ? null : (await ReadBodyAsync(response)).GetProperty("code").GetString());
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static string?[] Grants(JsonElement row) =>
        row.GetProperty("capabilities").EnumerateArray().Select(grant => grant.GetString()).ToArray();

    private sealed record Target(Guid Id, uint Version);

    private sealed record Fixture(NpgsqlConnectionStringBuilder Runtime, P0106Tests.TestClock Clock,
        LicensingApiFactory Api, P0114Tests.FixtureMailer Mailer, string Password, string Seed, string[] Codes) : IAsyncDisposable
    {
        internal DateTimeOffset OwnerProofAt { get; private set; }

        internal Task<P0110CookieBrowser> SignInOwnerAsync()
        {
            OwnerProofAt = Clock.GetUtcNow();
            return P0114Tests.SignInAsync(Api, Clock, BootstrapEmail, Password, Seed);
        }

        internal async Task<string> EnrollOperatorAsync(P0110CookieBrowser owner)
        {
            await P0114Tests.IssueAsync(owner, OperatorEmail);
            using var enrollment = P0114Tests.EnrollmentClient(Api);
            using var accepted = await P0114Tests.AcceptAsync(enrollment, Mailer.Token);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            return await P0114Tests.CompleteEnrollmentAsync(enrollment, Clock);
        }

        internal Task<P0110CookieBrowser> SignInOperatorAsync(string seed)
        {
            Clock.Advance(TimeSpan.FromSeconds(30));
            return P0114Tests.SignInAsync(Api, Clock, OperatorEmail, P0114Tests.InviteePassword, seed);
        }

        internal Task<object?> ScalarAsync(string sql) => LicensingPostgreSqlFixture.ReadScalarAsync(Runtime, sql);
        public ValueTask DisposeAsync() => Api.DisposeAsync();
    }
}
