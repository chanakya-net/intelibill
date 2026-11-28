using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0702Tests
{
    [Theory]
    [InlineData(false, false, "Uncertain")]
    [InlineData(true, false, "SmtpAccepted")]
    [InlineData(true, true, "Uncertain")]
    public async Task IndependentReplicaFencesSubmissionAndRecoversLostReply(bool acknowledge, bool expire, string outcome)
    {
        await using var smtp = new P0702SmtpPeer();
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await FixedLeaseFixtureAsync(smtp.Port, clock);
        var contact = await ApproveStandaloneAsync(fixture);
        var sending = DeliverAsync(fixture);
        await smtp.Received;
        Assert.Equal("Sending", await ProofStateAsync(fixture, contact));
        clock.Now = clock.Now.AddSeconds(60).AddTicks(-10);
        Assert.False(await DeliverAsync(fixture));
        if (expire)
        {
            clock.Now = clock.Now.AddTicks(10);
            Assert.True(await DeliverAsync(fixture));
            Assert.Equal("Uncertain", await ProofStateAsync(fixture, contact));
        }
        smtp.Finish(acknowledge);
        Assert.True(await sending);
        Assert.Equal(outcome, await ProofStateAsync(fixture, contact));
        clock.Now = clock.Now.AddDays(1);
        Assert.False(await DeliverAsync(fixture));
        Assert.Equal($"Leased,Sending,{outcome}", await fixture.SourceScalarAsync($"""
            SELECT string_agg(e.state, ',' ORDER BY CASE e.state WHEN 'Leased' THEN 1 WHEN 'Sending' THEN 2 ELSE 3 END)
            FROM notification_email_events e JOIN notification_email_deliveries d ON e.delivery_id = d.id
            WHERE d.proof_contact_id = '{contact:D}'
            """));
    }

    [Fact]
    public async Task ConfirmedConnectFailureRetriesOnlyAtDeadlineAndStopsAfterFiveAttempts()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        using var reserved = new System.Net.Sockets.TcpClient();
        reserved.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)reserved.Client.LocalEndPoint!).Port;
        await using var fixture = await FixedLeaseFixtureAsync(port, clock);
        var contact = await ApproveStandaloneAsync(fixture);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.True(await DeliverAsync(fixture));
            Assert.Equal(attempt == 5 ? "Failed" : "RetryPending", await ProofStateAsync(fixture, contact));
            clock.Now = clock.Now.AddSeconds(30 << (attempt - 1)).AddTicks(-10);
            Assert.False(await DeliverAsync(fixture));
            clock.Now = clock.Now.AddTicks(10);
        }
        Assert.False(await DeliverAsync(fixture));
    }

    [Theory]
    [InlineData(451, "RetryPending")]
    [InlineData(550, "Failed")]
    public async Task ExplicitSmtpRejectionKeepsOnlyTheSafeOutcome(int code, string expected)
    {
        await using var smtp = new P0702SmtpPeer();
        var logs = new P0107Tests.CapturedLogs();
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await FixedLeaseFixtureAsync(smtp.Port, clock, logs);
        var address = P0202Fixture.UniqueEmail();
        var contact = await ApproveStandaloneAsync(fixture, address);
        var send = DeliverAsync(fixture);
        await smtp.Received;
        smtp.Reject(code);
        Assert.True(await send);
        Assert.Equal(expected, await ProofStateAsync(fixture, contact));
        var history = (string)(await fixture.SourceScalarAsync("""
            SELECT jsonb_agg(to_jsonb(e))::text FROM notification_email_events e
            """))!;
        Assert.Contains("SmtpRejected", history);
        Assert.DoesNotContain("confidential-fixture-reply", history);
        Assert.DoesNotContain(logs.Events, entry => entry.Contains("confidential-fixture-reply") || entry.Contains(address));
    }

    private Task<P0213Fixture> FixedLeaseFixtureAsync(int port, P0201Clock clock, P0107Tests.CapturedLogs? logs = null)
    {
        var options = mailpit.BillingSettings();
        options["AdminMail:Host"] = "127.0.0.1";
        options["AdminMail:Port"] = port.ToString(CultureInfo.InvariantCulture);
        return P0213Fixture.StartAsync(database, licensingExtras: options,
            licensingServices: services =>
            {
                services.AddKeyedSingleton<TimeProvider>("billing-email-lease-clock", clock);
                if (logs is not null)
                    services.AddSingleton<ILogEventSink>(logs);
            });
    }

    private static async Task<Guid> ApproveStandaloneAsync(P0213Fixture fixture, string? address = null)
    {
        var owner = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        using var response = await owner.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(Guid.NewGuid(), null, address ?? P0202Fixture.UniqueEmail(), "en-IN"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BillingContactV1>())!.ContactId;
    }

    private static async Task<bool> DeliverAsync(P0213Fixture fixture)
    {
        await using var scope = fixture.Hosts.Licensing.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBillingEmailDispatcher>().DeliverOneAsync(CancellationToken.None);
    }

    private static Task<object?> ProofStateAsync(P0213Fixture fixture, Guid contact) =>
        fixture.SourceScalarAsync($"SELECT state FROM notification_email_deliveries WHERE proof_contact_id = '{contact:D}'");
}
