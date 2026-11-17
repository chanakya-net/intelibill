using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0306Tests
{
    [Fact]
    public void ManualPaymentCompatibilityVectorsKeepTheOriginalOnlineContract()
    {
        using var vectors = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/manual-payment-v1.json")));
        var original = vectors.RootElement.GetProperty("online");
        foreach (var item in vectors.RootElement.GetProperty("cases").EnumerateArray())
        {
            var candidate = JsonNode.Parse(original.GetRawText())!.AsObject();
            foreach (var change in item.GetProperty("patch").EnumerateObject())
                candidate[change.Name] = JsonNode.Parse(change.Value.GetRawText());
            var status = candidate.Deserialize<CheckoutPaymentV1>(JsonSerializerOptions.Web)!;
            Assert.Equal(item.GetProperty("valid").GetBoolean(), CheckoutPaymentsV1.HasValidPaymentSource(status));
            Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(status, JsonSerializerOptions.Web).Length, 1, CheckoutPaymentsV1.MaxStatusBytes);
        }
    }

    [Fact]
    public async Task ManualPaymentContractPreservesCheckoutRecoveryShapes()
    {
        var now = P0210Fixture.Start;
        await using var fixture = await StartAsync(database, new P0201Clock { Now = now }, Settings);
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var key = Guid.NewGuid();
        var started = await StartAsync(payer.Client, quote, HttpStatusCode.Created, key);
        var attempt = started.GetProperty("attemptId").GetGuid();
        Assert.False(started.TryGetProperty("manualPayment", out _));
        AssertStatus(started, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "PendingVerification", "NotFulfilled", "NotApplicable");

        using var billing = await SignInBillingAsync(fixture);
        var command = Receipt(quote, now);
        await ConfirmAsync(billing, Path(quote), command, "Fulfilled");
        var mixed = await StartAsync(payer.Client, quote, HttpStatusCode.OK, key);
        AssertStatus(mixed, "PendingVerification", "Fulfilled", "Applying");
        Assert.Equal(attempt, mixed.GetProperty("attemptId").GetGuid());
        Assert.Equal(JsonValueKind.Null, mixed.GetProperty("verifiedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, mixed.GetProperty("launch").ValueKind);
        AssertVerifiedSummary(mixed, command);
        await PostAsync(payer.Client, CapturePath(quote, attempt), new { outcome = "Failed" });
        var failed = await VerifyAsync(payer.Client, quote, attempt);
        AssertStatus(failed, "Failed", "Fulfilled", "Applying");
        Assert.Equal(attempt, failed.GetProperty("attemptId").GetGuid());
        AssertVerifiedSummary(failed, command);
        using var newAttempt = await payer.Client.PostAsJsonAsync( AttemptsPath(quote), Start(quote));
        Assert.Equal(HttpStatusCode.Conflict, newAttempt.StatusCode);
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts"));
    }

    [Fact]
    public async Task ManualSettlementLeavesTheLostOnlineAttemptForItsOriginalWorker()
    {
        var now = P0210Fixture.Start;
        var clock = new P0201Clock { Now = now };
        var gateway = new P0302Tests.GatewayProbe { LoseCreateResponse = true, LookupNotFound = true };
        await using var fixture = await StartAsync(database, clock, Settings, gateway.Install);
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        gateway.Database = fixture.Hosts.LicensingOwner;
        gateway.Account = account;
        var key = Guid.NewGuid();
        var started = await StartAsync(payer.Client, quote, HttpStatusCode.Created, key);
        var attempt = started.GetProperty("attemptId").GetGuid();
        AssertStatus(started, "PendingVerification", "NotFulfilled", "NotApplicable");

        using var billing = await SignInBillingAsync(fixture);
        await ConfirmAsync(billing, Path(quote), Receipt(quote, now), "Fulfilled");
        Assert.Equal(1L, await fixture.CountAsync("pending_payment_attempts",
            $"attempt_id = '{attempt:D}' AND NOT completed AND attempts = 0"));
        gateway.LookupNotFound = false;
        await P0303Tests.CaptureOriginalSessionAsync(fixture, attempt);
        clock.Now = clock.Now.AddSeconds(30);
        var lease = Assert.IsType<Application.Payments.PendingPaymentLease>(
            await P0303Tests.ClaimAsync(fixture.Hosts.Licensing.Services, clock));
        Assert.Equal(attempt, lease.AttemptId);
        Assert.True(await P0303Tests.ApplyLeaseAsync(fixture.Hosts.Licensing.Services, lease));
        Assert.Equal(1L, await fixture.CountAsync("pending_payment_attempts", $"attempt_id = '{attempt:D}' AND completed"));
        Assert.Equal(gateway.OriginalReference, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            $"SELECT provider_order_reference FROM public.payment_attempt_sessions WHERE attempt_id = '{attempt:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions"));
        Assert.Equal(1L, await fixture.CountAsync("payment_events", "kind = 'PaidNeedsReview'"));
        AssertStatus(await StartAsync(payer.Client, quote, HttpStatusCode.OK, key), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 1);
    }

    private static void AssertVerifiedSummary(JsonElement json, Application.Payments.ConfirmManualPayment command)
    {
        var body = json.GetRawText();
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(body), 1, CheckoutPaymentsV1.MaxStatusBytes);
        Assert.DoesNotContain(command.Reference, body);
        Assert.DoesNotContain(command.Evidence, body);
        Assert.DoesNotContain(command.Reason, body);
        var status = json.Deserialize<CheckoutPaymentV1>(JsonSerializerOptions.Web)!;
        Assert.Equal("Verified", status.ManualPayment!.Payment);
        Assert.Equal(command.ReceivedAt, status.ManualPayment.VerifiedAt);
        Assert.False(status.CanStart);
    }

    [Fact]
    public async Task PostgreSqlUpgradeRetainsOnlineEvidenceAndRejectsInvalidSources()
    {
        const string previous = "20261117100000_PendingPaymentReconciliation";
        await using var fixture = await StartAsync(database, new P0201Clock { Now = P0210Fixture.Start }, Settings);
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(payer.Client, quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "Verified", "Fulfilled", "Applying");
        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        var before = Assert.IsType<string>(await ReadOnlineEvidenceAsync(fixture));
        await migrator.MigrateAsync();
        Assert.Equal(before, await ReadOnlineEvidenceAsync(fixture));
        var error = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
            """
            INSERT INTO public.payment_fulfilments (account_id, order_id, term_id, policy_version, fulfilled_at)
            VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'payment-fulfilment/1', now())
            """));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal("payment_fulfilment_one_source", error.ConstraintName);
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, RuntimeGrant);
        using var billing = await SignInBillingAsync(fixture);
        await ConfirmAsync(billing, Path(quote), Receipt(quote, P0210Fixture.Start), "NeedsReview");
        var both = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
            """
            INSERT INTO public.payment_fulfilments (account_id, order_id, attempt_id, manual_receipt_id, term_id, policy_version, fulfilled_at)
            SELECT account_id, order_id, attempt_id, (SELECT receipt_id FROM public.manual_payment_receipts),
                gen_random_uuid(), policy_version, fulfilled_at FROM public.payment_fulfilments
            """));
        Assert.Equal("payment_fulfilment_one_source", both.ConstraintName);
        var retained = await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync(previous));
        Assert.Equal("55000", Assert.IsType<PostgresException>(retained.InnerException).SqlState);
        Assert.Equal(before, await ReadOnlineEvidenceAsync(fixture));
        output.WriteLine("Populated #866 schema upgraded with identical attempt, session, verified money, pending lease and fulfilment. PostgreSQL rejected both/no sources with 23514 and downgrade with manual evidence with 55000.");
    }

    private static Task<object?> ReadOnlineEvidenceAsync(P0302Fixture fixture) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner, """
            SELECT jsonb_build_object('attempt', to_jsonb(a), 'session', to_jsonb(s), 'payment', to_jsonb(v),
                'lease', to_jsonb(p), 'fulfilment', to_jsonb(f) - 'manual_receipt_id'
                    -- #871 adds nullable renewal provenance; a first purchase keeps it null and is otherwise unchanged.
                    - ARRAY['predecessor_term_id', 'payment_effective_at', 'grace_days_included', 'renewal_policy_version'])::text
            FROM public.payment_attempts a JOIN public.payment_attempt_sessions s USING (attempt_id)
            JOIN public.verified_payments v USING (attempt_id) JOIN public.pending_payment_attempts p USING (attempt_id)
            JOIN public.payment_fulfilments f USING (attempt_id)
            """);
}
