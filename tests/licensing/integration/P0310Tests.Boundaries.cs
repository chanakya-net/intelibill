using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Intelibill.Licensing.Application.Payments;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Fact]
    public async Task SelectedProviderStillHonorsTheExactQuoteAcceptanceBoundary()
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, Settings());
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await SelectProfileAsync(owner, "a", 0);
        var before = await fixture.AcceptedQuoteAsync(1);
        var boundary = await fixture.AcceptedQuoteAsync(1);
        Assert.Equal(before.Quote.ExpiresAt, boundary.Quote.ExpiresAt);
        clock.Now = before.Quote.ExpiresAt.AddTicks(-10);
        await StartAsync(before.Payer.Client, before.Quote, HttpStatusCode.Created);
        clock.Now = boundary.Quote.ExpiresAt;
        using var refused = await boundary.Payer.Client.PostAsJsonAsync(AttemptsPath(boundary.Quote), Start(boundary.Quote));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Billing.CheckoutExpired", await P0206Fixture.CodeAsync(refused));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", "provider_account_id = 'simulator-a'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions"));
    }

    [Fact]
    public async Task RetainedSimulatorProfilesAreNeverUsableInProduction()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, Settings());
        await using var production = fixture.Hosts.Licensing.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var scope = production.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>();
        Assert.Null(resolver.ForNewAttempts());
        Assert.Null(resolver.ForPin(A));
        var profiles = scope.ServiceProvider.GetRequiredService<IPaymentProviderRegistry>().Profiles();
        Assert.Contains(profiles, p => p.Pin == A && !p.Available && p.Blocker == "SimulatorForbidden");
    }
}
