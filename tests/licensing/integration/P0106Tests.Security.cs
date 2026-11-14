using System.Net;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0106Tests
{
    [Fact]
    public async Task BootstrapAttempts_AreDurablyBoundedAndSafelyAudited()
    {
        var runtime = await NewFixtureAsync();
        var clock = new TestClock();
        var proof = await PrepareAsync(runtime, clock);
        var password = NewPassword();
        await using (var api = new LicensingApiFactory(runtime, clock: clock))
        {
            using var client = Client(api);
            var csrf = await ReadCsrfAsync(client);
            using var badOrigin = await PostAsync(client, BootstrapPath, csrf,
                new { proofToken = proof, password }, "https://elsewhere.test");
            using var badCsrf = await SubmitAsync(client, "bad-csrf", proof, password);
            Assert.Equal(HttpStatusCode.Forbidden, badOrigin.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, badCsrf.StatusCode);
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var refused = await SubmitAsync(client, csrf, "malformed-" + attempt, password);
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                Assert.True(!(await refused.Content.ReadAsStringAsync()).Contains(password, StringComparison.Ordinal));
            }
        }

        await using var restarted = new LicensingApiFactory(runtime, clock: clock);
        using var browser = Client(restarted);
        var token = await ReadCsrfAsync(browser);
        using var limited = await SubmitAsync(browser, token, proof, password);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(15), limited.Headers.RetryAfter?.Delta);
        Assert.Equal(5L, await ScalarAsync(runtime, "SELECT count(*) FROM provider_audit_events WHERE action = 4"));
        Assert.Equal(0L, await ScalarAsync(runtime, "SELECT count(*) FROM admin_identities"));
        await AssertImmutableAsync(runtime);

        clock.Advance(TimeSpan.FromMinutes(15));
        using var setup = await SubmitAsync(browser, token, proof, password);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.True(setup.Headers.CacheControl?.NoStore);
        var cookie = ReadEnrollmentCookie(setup);
        Assert.Contains("secure", cookie.Attributes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie.Attributes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie.Attributes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie.Attributes, StringComparison.OrdinalIgnoreCase);
        await AssertTerminalAsync(runtime);
    }

    private static async Task AssertImmutableAsync(NpgsqlConnectionStringBuilder runtime)
    {
        foreach (var sql in new[]
        {
            "UPDATE provider_audit_events SET action = 1",
            "DELETE FROM provider_audit_events",
        })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(runtime, sql));
            Assert.Equal("55000", failure.SqlState);
        }
    }

    private static async Task AssertTerminalAsync(NpgsqlConnectionStringBuilder runtime)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            LicensingPostgreSqlFixture.ExecuteAsync(runtime, "UPDATE initial_admin_bootstrap SET consumed_at = NULL"));
        Assert.Equal("55000", failure.SqlState);
    }
}
