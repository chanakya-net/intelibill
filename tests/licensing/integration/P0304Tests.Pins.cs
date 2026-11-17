using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// The shared settlement contract for currency and pins. Each provider is refused at the seam where the mismatch can
/// really arise: Razorpay reports a foreign currency itself, while simulator persistence admits only INR, so its
/// currency refusal is proven on the observation the adapter returns. A stored attempt on any other pin is never served.
/// </summary>
public sealed partial class P0304Tests
{
    public static TheoryData<string, string> ProviderPinDimensions => new()
    {
        { "simulator", "provider_account_id" }, { "simulator", "environment" }, { "simulator", "configuration_version" },
        { "razorpay", "provider_account_id" }, { "razorpay", "environment" }, { "razorpay", "configuration_version" },
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ContractForeignCurrencyCaptureGrantsNothing(string provider)
    {
        await using var run = await ContractRun.StartAsync(database, provider);
        await run.CaptureAsync();
        if (run.Fake is { } fake)
        {
            fake.Payment(run.CapturedPayment!)["currency"] = "USD";
            Assert.NotEqual("Fulfilled", (await run.VerifyAsync(null, null)).GetProperty("fulfilment").GetString());
        }
        else
        {
            var settlement = await SettlementAsync(run);
            var refused = await settlement.FulfilAsync(settlement.Observation with { Currency = "USD" });
            Assert.Equal(FulfilmentOutcome.Rejected, refused);
        }
        Assert.Equal(0L, await run.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await run.Fixture.CountAsync("payment_fulfilments"));
    }

    [Theory]
    [MemberData(nameof(ProviderPinDimensions))]
    public async Task ContractObservationOnAnotherPinIsRefusedAtFulfilment(string provider, string dimension)
    {
        await using var run = await ContractRun.StartAsync(database, provider);
        await run.CaptureAsync();
        var settlement = await SettlementAsync(run);
        var pin = settlement.Observation.Pin;
        var other = dimension switch
        {
            "provider_account_id" => pin with { ProviderAccountId = pin.ProviderAccountId + "-other" },
            "environment" => pin with { Environment = pin.Environment == "Live" ? "Test" : "Live" },
            _ => pin with { ConfigurationVersion = pin.ConfigurationVersion + "-other" },
        };

        Assert.Equal(FulfilmentOutcome.Rejected, await settlement.FulfilAsync(settlement.Observation with { Pin = other }));
        Assert.Equal(0L, await run.Fixture.CountAsync("verified_payments"));
        Assert.Equal(FulfilmentOutcome.Fulfilled, await settlement.FulfilAsync(settlement.Observation));
        await P0302Tests.AssertSingleFulfilmentAsync(run.Fixture, run.Quote, 1);
    }

    [Theory]
    [MemberData(nameof(ProviderPinDimensions))]
    public async Task ContractOutstandingAttemptPinnedElsewhereIsNeverServedByTheActiveAdapter(string provider, string dimension)
    {
        await using var run = await ContractRun.StartAsync(database, provider);
        await run.CaptureAsync();
        await RepinAttemptAsync(run, dimension);
        var calls = run.Fake?.Calls.Count;

        foreach (var outcome in new[] { null, "Success" })
            Assert.NotEqual("Fulfilled", (await run.VerifyAsync(outcome, null)).GetProperty("fulfilment").GetString());
        Assert.Equal(calls, run.Fake?.Calls.Count);
        Assert.Equal(0L, await run.Fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await run.Fixture.CountAsync("payment_fulfilments"));
        Assert.Equal(0L, await run.Fixture.CountAsync("subscription_coverage_terms", "mode IS NOT NULL AND true"));
    }

    /// <summary>Simulates an attempt pinned under an earlier configuration: one pin column differs from the runtime's.</summary>
    private static Task RepinAttemptAsync(ContractRun run, string dimension)
    {
        var value = dimension == "environment" ? "'Live'" : $"{dimension} || '-retired'";
        return LicensingPostgreSqlFixture.ExecuteAsync(run.Fixture.Hosts.LicensingOwner, $"""
            SET session_replication_role = replica;
            UPDATE public.payment_attempts SET {dimension} = {value} WHERE attempt_id = '{run.Attempt:D}';
            SET session_replication_role = DEFAULT;
            """);
    }

    private static async Task<Settlement> SettlementAsync(ContractRun run)
    {
        var services = run.Fixture.Hosts.Licensing.Services;
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPaymentStore>();
        var loaded = await store.InAttemptScopeAsync<StoredPaymentAttempt>(run.Quote.AccountId, run.Attempt, false,
            async token => await store.FindAttemptAsync(run.Quote.AccountId, run.Attempt, false, token) is { } found
                ? found : ErrorOr.Error.NotFound(), CancellationToken.None);
        var attempt = loaded.Value;
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>().ForPin(attempt.Pin)!;
        var request = new CheckoutRequest(attempt.AccountId, attempt.OrderId, attempt.AttemptId, attempt.AmountPaise, attempt.Currency);
        var observed = await gateway.VerifyCheckoutAsync(
            new CheckoutVerification(attempt.ProviderOrderReference!, attempt.AmountPaise, attempt.Currency, request), CancellationToken.None);
        Assert.Equal(GatewayOutcome.Succeeded, observed.Outcome);
        Assert.Equal(ObservedPaymentState.Captured, observed.Value!.State);
        return new(services, attempt, observed.Value);
    }

    private sealed record Settlement(IServiceProvider Services, StoredPaymentAttempt Attempt, PaymentObservation Observation)
    {
        internal async Task<FulfilmentOutcome> FulfilAsync(PaymentObservation observation)
        {
            await using var scope = Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<FulfilPaidOrderHandler>()
                .HandleAsync(Attempt, observation, CancellationToken.None);
            Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
            return result.Value;
        }
    }
}
