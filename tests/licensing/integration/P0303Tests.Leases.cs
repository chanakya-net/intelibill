using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0303Tests
{
    [Fact]
    public async Task RestartedWorkerReconcilesOriginalAttemptWithoutHoldingDatabaseLocks()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        var probe = new P0302Tests.GatewayProbe();
        await using var fixture = await P0302Fixture.StartAsync(database, clock, services: services => probe.Install(services));
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1);
        probe.Database = fixture.Hosts.LicensingOwner;
        probe.Account = account;
        var key = Guid.NewGuid();
        var attempt = (await StartAsync(payer.Client, quote, System.Net.HttpStatusCode.Created, key)).GetProperty("attemptId").GetGuid();
        var reference = probe.OriginalReference;
        probe.DisableNewAttempts = true;
        await CaptureAsync(payer.Client, quote, attempt);
        var services = fixture.Hosts.Licensing.Services;
        Assert.Null(await ClaimAsync(services, clock));
        clock.Now = clock.Now.AddSeconds(30);
        var claim = Assert.IsType<PendingPaymentLease>(await ClaimAsync(services, clock));
        Assert.Equal(attempt, claim.AttemptId);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.BeforeVerify = async ct =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
        };

        var first = ApplyLeaseAsync(services, claim);
        PendingPaymentLease recovered;
        await using var restarted = fixture.Hosts.Licensing.WithWebHostBuilder(_ => { });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ProveDatabaseAvailableAsync(fixture, account);
            Assert.Null(await ClaimAsync(restarted.Services, clock));
            clock.Now = clock.Now.AddSeconds(30).AddTicks(-10);
            Assert.Null(await ClaimAsync(restarted.Services, clock));
            clock.Now = clock.Now.AddTicks(10);
            recovered = Assert.IsType<PendingPaymentLease>(await ClaimAsync(restarted.Services, clock));
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.NotEqual(claim.Token, recovered.Token);
        Assert.Equal(claim.AttemptId, recovered.AttemptId);
        Assert.False(await first);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        await Task.WhenAll(ApplyLeaseAsync(restarted.Services, recovered), VerifyAsync(payer.Client, quote, attempt));
        await AssertLeaseOutcomeAsync(fixture, quote, key, attempt, reference, clock.Now);
    }

    [Fact]
    public async Task RetryBudgetLeavesPaymentPendingAndExplicitVerificationCanStillRecover()
    {
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0302Fixture.StartAsync(database, clock);
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(payer.Client, quote, System.Net.HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await PostAsync(payer.Client, CapturePath(quote, attempt), new { outcome = "Timeout" });
        var services = fixture.Hosts.Licensing.Services;
        clock.Now = clock.Now.AddSeconds(30);
        var first = Assert.IsType<PendingPaymentLease>(await ClaimAsync(services, clock));
        Assert.False(await ApplyLeaseAsync(services, first));
        clock.Now = clock.Now.AddSeconds(4).AddTicks(-10);
        Assert.Null(await ClaimAsync(services, clock));
        clock.Now = clock.Now.AddTicks(10);

        for (var i = 1; i < 12; i++)
        {
            var lease = Assert.IsType<PendingPaymentLease>(await ClaimAsync(services, clock));
            Assert.Equal(attempt, lease.AttemptId);
            Assert.False(await ApplyLeaseAsync(services, lease));
            clock.Now = clock.Now.AddMinutes(5);
        }
        Assert.Null(await ClaimAsync(services, clock));
        Assert.Equal(1L, await fixture.CountAsync("pending_payment_attempts", "attempts = 12 AND NOT completed AND lease_token IS NULL"));
        AssertStatus(await StatusAsync(payer.Client, quote), "PendingVerification", "NotFulfilled", "NotApplicable");

        await CaptureAsync(payer.Client, quote, attempt);
        AssertStatus(await VerifyAsync(payer.Client, quote, attempt), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1L, await fixture.CountAsync("pending_payment_attempts", "attempts = 12 AND completed"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions"));
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 1);
    }

    private static async Task ProveDatabaseAvailableAsync(P0302Fixture fixture, Guid account)
    {
        await using var connection = new NpgsqlConnection(fixture.Hosts.Fixture.Runtime.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT set_config('app.billing_account_id', @account, true);
            SELECT pg_try_advisory_xact_lock(hashtextextended('b' || @account, 0));
            SELECT count(*) FROM public.claim_pending_payment(now(), gen_random_uuid());
            """, connection, transaction);
        command.Parameters.AddWithValue("account", account.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        await reader.DisposeAsync();
        await transaction.CommitAsync();
    }

    internal static async Task<PendingPaymentLease?> ClaimAsync(IServiceProvider root, TimeProvider clock)
    {
        await using var scope = root.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPendingPaymentAttemptStore>()
            .ClaimAsync(clock.GetUtcNow(), CancellationToken.None);
    }

    internal static async Task<bool> ApplyLeaseAsync(IServiceProvider root, PendingPaymentLease lease)
    {
        await using var scope = root.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<ReconcilePaymentHandler>()
            .HandleLeaseAsync(lease, CancellationToken.None);
        Assert.False(outcome.IsError, outcome.IsError ? outcome.FirstError.Code : null);
        return outcome.Value;
    }

    private static async Task AssertLeaseOutcomeAsync(P0302Fixture fixture, Intelibill.Shared.Contracts.V1.CheckoutQuoteV1 quote,
        Guid key, Guid attempt, string? reference, DateTimeOffset recoveredAt)
    {
        var where = $"attempt_id = '{attempt:D}'";
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 1);
        Assert.Equal(1L, await fixture.CountAsync("pending_payment_attempts", $"{where} AND completed AND attempts = 2"));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", $"{where} AND operation_key = '{key:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions", where));
        Assert.Equal(reference, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            $"SELECT provider_order_reference FROM public.payment_attempt_sessions WHERE {where}"));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments",
            $"{where} AND provider_account_id = 'local-simulator' AND environment = 'Development' AND configuration_version = 'development-simulator/1'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilment_deliveries", $"order_id = '{quote.OrderId:D}'"));

        var dir = Environment.GetEnvironmentVariable("P0303_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir))
            await File.WriteAllTextAsync(Path.Combine(dir, "worker-restart-and-races.json"), JsonSerializer.Serialize(new
            {
                key, attempt, reference, recoveredAt, provider = "development-simulator", account = "local-simulator",
                environment = "Development", configuration = "development-simulator/1",
                claims = 2, leaseSeconds = 30, staleClaimApplied = false, providerSessions = 1, verifiedPayments = 1,
                fulfilments = 1, termFacts = 1, shopLinks = 1, entitlementPublications = 1,
                independentRuntimeTransactionCommittedDuringIo = true,
                freshLicensingHost = true, samePostgreSqlDatabase = true,
            }));
    }
}
