using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0109Tests
{
    [Fact]
    public async Task AbsentAccountsAndMalformedIdentifiersReceiveTheSameSignInRejection()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: Settings());
        var (browser, password, _) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using var owner = browser;
        var csrf = await ReadCsrfAsync(browser);
        foreach (var email in new[] { "absent@example.test", "invalid-email" })
        {
            using var response = await PostAsync(browser, password, "000000", csrf, email: email);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime, "SELECT count(*) FROM admin_sessions"));
    }

    [Fact]
    public async Task ReplicasConsumeTheSameAuthenticatorStepOnlyOnceAndPersistOneSession()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var settings = Settings();
        var clock = new P0106Tests.TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        await using var replica = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        var (browser, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using var owner = browser;
        using var second = replica.CreateClient(new() { BaseAddress = new Uri(Origin) });
        var csrf = await ReadCsrfAsync(browser);
        var secondCsrf = await ReadCsrfAsync(second);
        var otp = P0108Tests.SignInOtp(seed, clock.GetUtcNow());

        var responses = await Task.WhenAll(PostAsync(browser, password, otp, csrf), PostAsync(second, password, otp, secondCsrf));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }

        Assert.Equal(1L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime, "SELECT count(*) FROM admin_sessions"));
        Assert.Equal(1L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime,
            "SELECT count(*) FROM provider_audit_events WHERE action = 14"));
    }

    [Fact]
    public async Task PasswordBudgetIsSharedAcrossReplicasAndResetsAtTheBoundary()
    {
        var runtime = await new P0108Tests(database).SignInDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var settings = Settings();
        await using var api = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        await using var replica = new LicensingApiFactory(runtime, clock: clock, settings: settings);
        var (browser, password, seed) = await P0108Tests.EnrollSignInOwnerAsync(api, clock);
        using var owner = browser;
        using var second = replica.CreateClient(new() { BaseAddress = new Uri(Origin) });
        var clients = new[] { (browser, await ReadCsrfAsync(browser)), (second, await ReadCsrfAsync(second)) };
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var (client, csrf) = clients[attempt % 2];
            using var refused = await PostAsync(client, "Wrong-fixture-password", "000000", csrf);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        using var limited = await PostAsync(second, password, P0108Tests.SignInOtp(seed, clock.GetUtcNow()), clients[1].Item2);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), limited.Headers.RetryAfter?.Delta);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var accepted = await PostAsync(second, password, P0108Tests.SignInOtp(seed, clock.GetUtcNow()), clients[1].Item2);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }
}
