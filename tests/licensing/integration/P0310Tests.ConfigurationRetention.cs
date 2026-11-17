using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Fact]
    public async Task LegacyBootstrapRetainsConfiguredReferencesWithoutInventingAnOwnerSelection()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, WebhookSettings());
        var purchase = await fixture.AcceptedQuoteAsync(1);
        await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created);
        Assert.Equal(2L, await fixture.CountAsync("payment_provider_bindings"));
        Assert.Equal(0L, await fixture.CountAsync("payment_provider_selections", "admin_id IS NOT NULL"));
        var config = fixture.Hosts.Licensing.Services.GetRequiredService<IConfiguration>();
        config["FixtureSecrets:Replacement"] = SecretA;
        config["Payments:Profiles:0:WebhookSecretReference"] = "FixtureSecrets:Replacement";
        using var scope = fixture.Hosts.Licensing.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetRequiredService<Intelibill.Licensing.Application.Payments.IPaymentGatewayResolver>().ForPin(A));
    }

    [Fact]
    public async Task AConfigurationVersionCannotBeReboundToAnotherCredentialReference()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, WebhookSettings());
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await SelectProfileAsync(owner, "a", 0);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        var config = fixture.Hosts.Licensing.Services.GetRequiredService<IConfiguration>();
        config["FixtureSecrets:Replacement"] = SecretA;
        config["Payments:Profiles:0:WebhookSecretReference"] = "FixtureSecrets:Replacement";
        AssertStatus(await StatusAsync(purchase.Payer.Client, purchase.Quote), "PendingVerification", "NeedsReview", "NotApplicable");
        AssertStatus(await VerifyAsync(purchase.Payer.Client, purchase.Quote, attempt), "PendingVerification", "NeedsReview", "NotApplicable");
        Assert.Equal(1L, await fixture.CountAsync("payment_routing_events", "reason = 'ConfigurationReused'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions"));
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        config["Payments:Profiles:0:WebhookSecretReference"] = "FixtureSecrets:A";
        await CaptureAsync(purchase.Payer.Client, purchase.Quote, attempt);
        AssertStatus(await VerifyAsync(purchase.Payer.Client, purchase.Quote, attempt), "Verified", "Fulfilled", "Applying");
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, purchase.Quote, 1);
        foreach (var sql in new[] { "UPDATE public.payment_provider_bindings SET webhook_reference = 'mutated-ref'",
            "DELETE FROM public.payment_provider_bindings", "TRUNCATE public.payment_provider_bindings",
            "DELETE FROM public.payment_provider_selections", "DELETE FROM public.payment_provider_routing" })
            Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, sql))).SqlState);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(
            fixture.Hosts.Fixture.Runtime, """
                INSERT INTO public.payment_provider_bindings (pin, profile_id, registered_at)
                VALUES ('{}'::jsonb, 'unscoped', now())
                """));
        Assert.Equal("42501", denied.SqlState);
    }
}
