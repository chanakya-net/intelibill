using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0106Tests
{
    private const string Origin = "https://localhost";
    private const string BootstrapPath = "/api/admin/auth/bootstrap";
    private const string Email = "fixture.owner@example.test";
    private static readonly TestClock Clock = new();
    private static string NewPassword() => "Fixture-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task ConcurrentBootstrap_CreatesExactlyOneInitialOwner()
    {
        var runtime = await NewFixtureAsync();
        await using var firstHost = new LicensingApiFactory(runtime, clock: Clock);
        await using var secondHost = new LicensingApiFactory(runtime, clock: Clock);
        using var first = Client(firstHost);
        using var second = Client(secondHost);
        var proof = await PrepareAsync(runtime);
        var firstCsrf = await ReadCsrfAsync(first);
        var secondCsrf = await ReadCsrfAsync(second);
        var password = NewPassword();
        var responses = await Task.WhenAll(SubmitAsync(first, firstCsrf, proof, password),
            SubmitAsync(second, secondCsrf, proof, password));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await AssertOwnerFactsAsync(runtime, proof, 1);
            using var replay = await SubmitAsync(first, firstCsrf, proof, password);
            Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
            Assert.True((await PrepareResultAsync(runtime)).IsError);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.OK, 1)]
    [InlineData(0, HttpStatusCode.Conflict, 0)]
    [InlineData(1, HttpStatusCode.Conflict, 0)]
    public async Task ProofExpiresAtTheExactBoundary(int offsetSeconds, HttpStatusCode expected, int owners)
    {
        var runtime = await NewFixtureAsync();
        var clock = new TestClock();
        await using var api = new LicensingApiFactory(runtime, clock: clock);
        using var client = Client(api);
        var proof = await PrepareAsync(runtime, clock);
        var csrf = await ReadCsrfAsync(client);
        clock.Advance(TimeSpan.FromSeconds(1800 + offsetSeconds));

        using var response = await SubmitAsync(client, csrf, proof, NewPassword());

        Assert.Equal(expected, response.StatusCode);
        await AssertOwnerFactsAsync(runtime, proof, owners);
    }

    private static async Task AssertOwnerFactsAsync(NpgsqlConnectionStringBuilder runtime, string proof, int owners)
    {
        Assert.Equal((long)owners, await ScalarAsync(runtime, """
            SELECT count(*) FROM admin_identities WHERE mfa_enrolled_at IS NULL AND verified_at IS NOT NULL
                AND length(password_hash) = 60
            """));
        Assert.Equal((long)owners, await ScalarAsync(runtime, """
            SELECT count(*) FROM initial_admin_bootstrap WHERE consumed_at IS NOT NULL AND owner_id IS NOT NULL
            """));
        Assert.Equal((long)owners, await ScalarAsync(runtime, "SELECT count(*) FROM provider_audit_events WHERE action = 3"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof))),
            await ScalarAsync(runtime, "SELECT proof_digest FROM initial_admin_bootstrap"));
    }

    private static Task<object?> ScalarAsync(NpgsqlConnectionStringBuilder runtime, string sql) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(runtime, sql);

    private static async Task<string> PrepareAsync(NpgsqlConnectionStringBuilder runtime, TestClock? clock = null)
    {
        var result = await PrepareResultAsync(runtime, clock);
        Assert.False(result.IsError);
        return result.Value.Value;
    }

    private static async Task<ErrorOr.ErrorOr<BootstrapSecret>> PrepareResultAsync(
        NpgsqlConnectionStringBuilder runtime, TestClock? clock = null)
    {
        var settings = new NpgsqlConnectionStringBuilder(runtime.ConnectionString)
            { Username = LicensingPostgreSqlFixture.MigratorRole };
        await using var preparer = new LicensingApiFactory(settings, clock: clock ?? Clock);
        await using var scope = preparer.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PrepareBootstrapAdminHandler>()
            .PrepareAsync(Email, "P01-06-fixture-attestation", Origin, false, CancellationToken.None);
    }

    private static HttpClient Client(LicensingApiFactory api) =>
        api.CreateClient(new() { BaseAddress = new Uri(Origin), AllowAutoRedirect = false });

    private static async Task<string> ReadCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri(BootstrapPath + "/context", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string csrf, string proof, string password) =>
        PostAsync(client, BootstrapPath, csrf, new { proofToken = proof, password });

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string csrf, object body,
        string origin = Origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Bootstrap-CSRF", csrf);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    internal sealed class TestClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero).Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        internal void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }
}
