using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0302Tests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Disabled")]
    public async Task LegacyBootstrapIsUnavailableOutsideLocalSimulatorMode(string? mode)
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start },
            settings: new Dictionary<string, string?> { ["Payments:Mode"] = mode });
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);

        var status = await StatusAsync(payer.Client, quote);
        AssertStatus(status, "NotStarted", "NotFulfilled", "NotApplicable");
        Assert.False(status.GetProperty("canStart").GetBoolean());
        using var start = await payer.Client.PostAsJsonAsync(AttemptsPath(quote), Start(quote));
        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
        Assert.Equal(CheckoutPaymentsV1.Unavailable, await P0206Fixture.CodeAsync(start));
        using var capture = await payer.Client.PostAsJsonAsync(CapturePath(quote, Guid.NewGuid()), new { outcome = "Captured" });
        Assert.Equal(HttpStatusCode.NotFound, capture.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts"));
        Assert.Equal(0L, await fixture.CountAsync("development_gateway_sessions"));
    }

    [Fact]
    public async Task ProviderIdentifiersAndStatusesStayInsideAdapters()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var started = await StartAsync(payer.Client, quote, HttpStatusCode.Created);
        var attempt = started.GetProperty("attemptId").GetGuid();
        await CaptureAsync(payer.Client, quote, attempt);
        var verified = await VerifyAsync(payer.Client, quote, attempt);

        string[] allowed = ["schemaVersion", "accountId", "orderId", "attemptId", "payment", "fulfilment", "access",
            "amountPaise", "currency", "startedAt", "verifiedAt", "termId", "termStartsAt", "termEndsAt", "shopsTotal",
            "shopsApplied", "canStart", "launch"];
        foreach (var body in new[] { started, verified })
        {
            Assert.Equal(allowed.Order(), body.EnumerateObject().Select(property => property.Name).Order());
            Assert.DoesNotContain("dev_", body.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("simulator\"", body.GetProperty("payment").GetRawText(), StringComparison.Ordinal);
        }
        var launch = started.GetProperty("launch");
        Assert.Equal(["flow", "parameters"], launch.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Object, launch.GetProperty("parameters").ValueKind);
        Assert.Empty(launch.GetProperty("parameters").EnumerateObject());
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", "provider_payment_reference LIKE 'dev_pay_%'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempt_sessions", "provider_order_reference LIKE 'dev_order_%'"));
    }

    /// <summary>
    /// A local historical attempt survives a change of Mode. Production refuses simulator recovery and exposes
    /// unpaid routing review without inventing verified funds or offering another attempt.
    /// </summary>
    [Theory]
    [InlineData("Testing", "Disabled")]
    [InlineData("Production", "LocalSimulator")]
    public async Task PersistedSimulatorRecoveryFollowsItsPinAndLocalEnvironment(string environment, string mode)
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var started = await StartAsync(payer.Client, quote, HttpStatusCode.Created);
        var attempt = started.GetProperty("attemptId").GetGuid();
        Assert.Equal(CheckoutPaymentsV1.SimulatorFlow, started.GetProperty("launch").GetProperty("flow").GetString());

        await using var licensing = fixture.Hosts.Licensing.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Payments:Mode"] = mode }));
        });
        await using var merchant = fixture.Hosts.Merchant.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddHttpClient("MerchantBillingDelegation")
                .ConfigurePrimaryHttpMessageHandler(() => licensing.Server.CreateHandler())));
        using var reopened = merchant.CreateClient();
        reopened.DefaultRequestHeaders.Authorization = payer.Client.DefaultRequestHeaders.Authorization;

        var status = await StatusAsync(reopened, quote);
        AssertStatus(status, environment == "Production" ? "PendingVerification" : "AwaitingPayment",
            environment == "Production" ? "NeedsReview" : "NotFulfilled", "NotApplicable");
        Assert.Equal(attempt, status.GetProperty("attemptId").GetGuid());
        Assert.Equal(environment == "Production" ? JsonValueKind.Null : JsonValueKind.Object, status.GetProperty("launch").ValueKind);
        Assert.False(status.GetProperty("canStart").GetBoolean());
        using var capture = await reopened.PostAsJsonAsync(CapturePath(quote, attempt), new { outcome = "Captured" });
        Assert.Equal(environment == "Production" ? HttpStatusCode.NotFound : HttpStatusCode.OK, capture.StatusCode);

        Assert.Equal(1L, await fixture.CountAsync("payment_attempts"));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempt_sessions", "provider_order_reference IS NOT NULL"));
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(attempt, (await StatusAsync(payer.Client, quote)).GetProperty("attemptId").GetGuid());
    }
}
