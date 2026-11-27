using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Serilog.Core;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0305Tests
{
    private static readonly string[] EvidenceKeys = ["type", "account", "occurredAt", "state", "amountPaise", "currency"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizeBodyIsRejectedWithOrWithoutContentLength(bool streamed)
    {
        await using var rig = await StartAsync();
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        var body = new string(' ', (64 * 1024) + 1);
        using var request = Delivery(body, "oversize", Secret);
        if (streamed)
            request.Content = new StreamContent(new UnseekableBody(Encoding.UTF8.GetBytes(body)));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0L, await rig.Fixture.CountAsync("gateway_inbox_events"));
        Assert.Empty(rig.Fake.Calls);
    }

    [Fact]
    public async Task UnconfiguredWebhookLeavesCheckoutAvailable()
    {
        await using var rig = await P0304Tests.Rig.StartAsync(database);
        await rig.StartAttemptAsync();
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        using var response = await SendAsync(client, "{}", "not_configured");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, rig.Fake.Posts);
        Assert.Equal(0L, await rig.Fixture.CountAsync("gateway_inbox_events"));
    }

    [Fact]
    public async Task RetiredSecretCanAuthenticateAndOnlyMinimalEvidenceIsRetainedOrLogged()
    {
        const string retired = "fixture-retired-868";
        var logs = new P0107Tests.CapturedLogs();
        await using var rig = await StartAsync(new Dictionary<string, string?>
        {
            ["Payments:Razorpay:RetiredWebhookSecrets:0"] = retired,
            ["Observability:Logging:MinimumLevel"] = "Verbose"
        }, services => services.AddSingleton<ILogEventSink>(logs));
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        var owner = rig.Fixture.Hosts.LicensingOwner;
        var body = Body(rig, "order_unknown", "pay_unknown");
        using var forged = await SendAsync(client, body, "event_1", "wrong-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(0L, await rig.Fixture.CountAsync("gateway_inbox_events"));
        using var accepted = await SendAsync(client, body, "event_1", retired);
        using var duplicate = await SendAsync(client, body, "event_1");
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, duplicate.StatusCode);
        await DrainAsync(rig.Fixture.Hosts.Licensing.Services, rig.Fixture.CheckoutClock);
        await AssertWorkAsync(rig, "Pending", "UnknownOrder", failure: true);

        const string receive = "public.receive_gateway_inbox_event(uuid,text,text,text,text,text,text,text,text,bytea,bytea,timestamptz)";
        await LicensingPostgreSqlFixture.ExecuteAsync(owner, $"REVOKE EXECUTE ON FUNCTION {receive} FROM licensing_runtime");
        using var unavailable = await SendAsync(client, body, "storage_unavailable");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_events"));
        Assert.Empty(rig.Fake.Calls);
        await AssertMinimalEvidenceAsync(rig, logs, body, retired);
    }

    private static async Task AssertMinimalEvidenceAsync(P0304Tests.Rig rig, P0107Tests.CapturedLogs logs, string body, string retired)
    {
        var owner = rig.Fixture.Hosts.LicensingOwner;
        var cipher = Assert.IsType<byte[]>(await LicensingPostgreSqlFixture.ReadScalarAsync(owner,
            "SELECT protected_evidence FROM gateway_inbox_events"));
        var protection = rig.Fixture.Hosts.Licensing.Services.GetRequiredService<IDataProtectionProvider>();
        using var minimal = JsonDocument.Parse(protection.CreateProtector("InteliBill.Licensing.GatewayInbox/1",
            "razorpay", "fixture-account", "Test", "fixture/1", "event_1").Unprotect(cipher));
        var evidence = minimal.RootElement;
        Assert.Equal(EvidenceKeys,
            evidence.EnumerateObject().Select(p => p.Name));
        Assert.Equal("payment.captured", evidence.GetProperty("type").GetString());
        Assert.Equal(Account, evidence.GetProperty("account").GetString());
        await logs.WaitForAsync("Payment webhook receipt unavailable.");
        Assert.Contains(logs.Events, line => line.Contains("Payment webhook authentication rejected.", StringComparison.Ordinal));
        var plaintext = await LicensingPostgreSqlFixture.ReadScalarAsync(owner, """
            SELECT row_to_json(e)::text || row_to_json(w)::text
            FROM gateway_inbox_events e JOIN gateway_inbox_work w USING (inbox_id)
            """);
        var scanned = string.Join('\n', [.. logs.Events, plaintext?.ToString(), evidence.GetRawText()]);
        foreach (var marker in new[] { PayerMarker, "fixture-private-868", Secret, retired, FakeRazorpay.KeySecret,
                     Sign(body, retired), Sign(body, Secret), body })
            Assert.DoesNotContain(marker, scanned, StringComparison.Ordinal);
        await RecordAsync("webhook-redaction.json", new
        {
            forged = 401, stored = 204, duplicate = 204, unavailable = 503, retainedRows = 1, providerCalls = 0,
            forbiddenMarkersFound = 0, sealedEvidenceKeys = evidence.EnumerateObject().Select(p => p.Name)
        });
    }

    [Fact]
    public async Task RetainedEvidencePreventsRewriteOrRuntimeAccess()
    {
        await using var rig = await StartAsync();
        var owner = rig.Fixture.Hosts.LicensingOwner;
        var runtime = rig.Fixture.Hosts.Fixture.Runtime;
        using var client = rig.Fixture.Hosts.Licensing.CreateClient();
        await DeliverAsync(client, Body(rig, "order_unknown", "pay_unknown"), "retained");

        // The local Compose role has blanket DML privileges. Forced RLS must still hide the inbox rows.
        await LicensingPostgreSqlFixture.ExecuteAsync(owner, """
            GRANT SELECT, INSERT, UPDATE, DELETE ON gateway_inbox_events, gateway_inbox_work TO licensing_runtime
            """);
        await AssertRuntimeDenialAsync(runtime);
        foreach (var sql in new[] { "UPDATE gateway_inbox_events SET kind = 'Other'", "DELETE FROM gateway_inbox_events",
                     "TRUNCATE gateway_inbox_events CASCADE", "DELETE FROM gateway_inbox_work" })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(owner, sql));
            Assert.Equal("55000", denied.SqlState);
        }
        Assert.Equal(1L, await rig.Fixture.CountAsync("gateway_inbox_events"));
    }

    private static async Task AssertRuntimeDenialAsync(NpgsqlConnectionStringBuilder runtime)
    {
        Assert.Equal(0L, Assert.IsType<long>(await LicensingPostgreSqlFixture.ReadScalarAsync(runtime,
            "SELECT count(*) FROM gateway_inbox_events")));
        Assert.Equal(0L, Assert.IsType<long>(await LicensingPostgreSqlFixture.ReadScalarAsync(runtime,
            "WITH changed AS (UPDATE gateway_inbox_work SET attempts = 0 RETURNING 1) SELECT count(*) FROM changed")));
        var denied = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(runtime,
            "SELECT public.requeue_gateway_inbox_event(gen_random_uuid(), now())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
    }

    private sealed class UnseekableBody(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
