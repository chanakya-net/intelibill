using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Serilog.Core;
using Serilog.Events;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P01-07 fixtures. Every identity is synthetic. The inviting owner's completed MFA is a
/// labeled fixture fact (P01-08 owns TOTP), and its session comes from the environment-guarded
/// fixture scheme, because no production provider session exists on this base.
/// </summary>
public sealed partial class P0107Tests(LicensingPostgreSqlFixture database, MailpitFixture mail)
    : IClassFixture<LicensingPostgreSqlFixture>, IClassFixture<MailpitFixture>
{
    private const string Origin = "https://localhost";
    private const string InvitationsPath = "/api/admin/invitations";
    private const string AcceptPath = "/api/admin/auth/invitation";
    private const string EnrollmentPath = "/api/admin/auth/bootstrap/enrollment";
    private const string BusinessPath = "/api/admin/access";
    private const string SessionCookie = "Intelibill.Admin.FixtureSession";
    private const string EnrollmentCookie = "Intelibill.Admin.BootstrapEnrollment";
    private const string Capability = "admin.invitations.manage";
    private static readonly Regex Link = new(@"https://localhost/auth/invitation#token=(i1_[A-Za-z0-9_-]{86})(?![A-Za-z0-9_-])");

    private sealed record Provider(NpgsqlConnectionStringBuilder Runtime, Guid OwnerId, string Session, P0106Tests.TestClock Clock);

    private static string NewPassword() => "Fixture-" + Guid.NewGuid().ToString("N");

    private static string NewInvitee() => $"invitee.{Guid.NewGuid():N}@example.test";

    private async Task<Provider> ProviderAsync()
    {
        var runtime = await NewDatabaseAsync();
        var clock = new P0106Tests.TestClock();
        var migrator = database.Login(runtime.Database!, LicensingPostgreSqlFixture.MigratorRole);
        await using (var preparer = new LicensingApiFactory(migrator, clock: clock))
        await using (var scope = preparer.Services.CreateAsyncScope())
        {
            var proof = await scope.ServiceProvider.GetRequiredService<PrepareBootstrapAdminHandler>()
                .PrepareAsync("fixture.owner@example.test", "P01-07-fixture-attestation", Origin, false, CancellationToken.None);
            var owner = await scope.ServiceProvider.GetRequiredService<BootstrapAdminHandler>()
                .BootstrapAsync(proof.Value.Value, NewPassword(), Origin, CancellationToken.None);
            Assert.False(owner.IsError);
        }

        // FIXTURE ONLY: P01-08 owns TOTP enrollment. The inviting owner's MFA is synthesized here.
        await LicensingPostgreSqlFixture.ExecuteAsync(migrator, "UPDATE admin_identities SET mfa_enrolled_at = verified_at");
        var ownerId = (Guid)(await ScalarAsync(runtime, "SELECT owner_id FROM initial_admin_bootstrap"))!;
        return new Provider(runtime, ownerId, "fixture-session-" + Guid.NewGuid().ToString("N"), clock);
    }

    private LicensingApiFactory Host(Provider provider, Action<Dictionary<string, string?>>? adjust = null,
        CapturedLogs? logs = null, string environment = "Testing")
    {
        var settings = mail.Settings();
        settings["AdminFixtures:SessionDigest"] = Digest(provider.Session);
        settings["AdminFixtures:AdminId"] = provider.OwnerId.ToString();
        settings["AdminFixtures:Capabilities"] = Capability;
        settings["AdminFixtures:MfaVerifiedAt"] = provider.Clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        settings["Observability:Logging:MinimumLevel"] = "Verbose";
        adjust?.Invoke(settings);
        return new LicensingApiFactory(provider.Runtime, environment: environment, clock: provider.Clock, settings: settings,
            configure: logs is null ? null : services => services.AddSingleton<ILogEventSink>(logs));
    }

    private static HttpClient Browser(LicensingApiFactory api, string? session = null)
    {
        var cookies = new CookieContainer();
        if (session is not null) cookies.Add(new Uri(Origin), new Cookie(SessionCookie, session));
        return api.CreateDefaultClient(new Uri(Origin), new CookieContainerHandler(cookies));
    }

    private static async Task<HttpResponseMessage> InviteAsync(HttpClient owner, string email, string origin = Origin,
        string? csrf = null)
    {
        csrf ??= await ReadTokenAsync(owner, InvitationsPath + "/context");
        return await PostAsync(owner, InvitationsPath, csrf, new { email }, origin);
    }

    private static async Task<HttpResponseMessage> AcceptAsync(HttpClient browser, string? token, string email, string password)
    {
        var csrf = await ReadTokenAsync(browser, AcceptPath + "/context");
        return await PostAsync(browser, AcceptPath + "/accept", csrf, new { invitationToken = token, email, password });
    }

    private static async Task<string> ReadTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string csrf, object body,
        string origin = Origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Bootstrap-CSRF", csrf);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    /// <summary>Invitation tokens captured for <paramref name="recipient"/>, newest first.</summary>
    private async Task<string[]> ReadLinkTokensAsync(string recipient, int expectedMessages = 1)
    {
        string[] bodies = [];
        for (var attempt = 0; attempt < 50 && bodies.Length < expectedMessages; attempt++)
        {
            bodies = await mail.ReadAsync(recipient);
            if (bodies.Length < expectedMessages) await Task.Delay(100);
        }
        Assert.Equal(expectedMessages, bodies.Length);
        return [.. bodies.Select(body =>
        {
            var link = Link.Match(body);
            Assert.True(link.Success);
            return link.Groups[1].Value;
        })];
    }

    private async Task<NpgsqlConnectionStringBuilder> NewDatabaseAsync()
    {
        var name = "p0107_" + Guid.NewGuid().ToString("N");
        await database.CreateMigratedLicensingDatabaseAsync(name);
        var migrator = database.Login(name, LicensingPostgreSqlFixture.MigratorRole);
        await LicensingPostgreSqlFixture.ExecuteAsync(migrator, """
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO licensing_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO licensing_runtime;
            """);
        return database.Login(name, LicensingPostgreSqlFixture.RuntimeRole);
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Task<object?> ScalarAsync(NpgsqlConnectionStringBuilder runtime, string sql) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(runtime, sql);

    private static (string Pair, string Attributes)? EnrollmentCookieOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        var value = values.SingleOrDefault(header => header.StartsWith(EnrollmentCookie + "=", StringComparison.Ordinal));
        if (value is null) return null;
        var separator = value.IndexOf(';', StringComparison.Ordinal);
        return (value[..separator], value[separator..]);
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return response.StatusCode;
    }

    internal sealed class CapturedLogs : ILogEventSink
    {
        private readonly ConcurrentQueue<string> _events = new();

        public void Emit(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            logEvent.RenderMessage(text, CultureInfo.InvariantCulture);
            foreach (var property in logEvent.Properties) text.Write($" {property.Key}={property.Value}");
            text.Write(logEvent.Exception?.ToString());
            _events.Enqueue(text.ToString());
        }

        internal string[] Events => [.. _events];

        internal async Task WaitForAsync(string fragment)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (_events.Any(entry => entry.Contains(fragment, StringComparison.Ordinal))) return;
                await Task.Delay(50);
            }

            Assert.Fail($"No captured log contained '{fragment}'.");
        }
    }
}
