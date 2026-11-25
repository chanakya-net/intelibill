using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Fact]
    public async Task OriginalCredentialRestorationIsRequiredBeforeTheOnlySend()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant, P0310Tests.WebhookSettings());
        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        await P0310Tests.SelectProfileAsync(owner, "a", 0);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        await P0310Tests.SelectProfileAsync(owner, "b", 1);
        var configuration = fixture.Hosts.Licensing.Services.GetRequiredService<IConfiguration>();
        configuration["FixtureSecrets:A"] = null;
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Reserved' AND reason='OriginalAdapterUnavailable'"));
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        configuration["FixtureSecrets:A"] = P0310Tests.SecretA;
        fixture.At(Instant.AddHours(1));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_refunds", "provider_account_id='simulator-a'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Confirmed'"));
    }

    [Fact]
    public async Task ExhaustedUncertainReadsHoldFundsUntilFinanceWakesOriginalLookup()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        var path = RequestPath(package.Account, request);
        using var pending = await P0212Fixture.PostAsync(finance, path + "/simulator", new { outcome = "Mismatch" });
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        for (var hour = 0; hour <= 14; hour++)
        {
            fixture.At(Instant.AddHours(hour));
            await PumpRefundsAsync(fixture);
        }
        Assert.Equal(1L, await fixture.CountAsync("refund_work", "state='Unknown' AND due_at='infinity'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(0L, await fixture.CountAsync("refund_applications"));
        var reads = await fixture.CountAsync("refund_observations");
        using var wake = await P0212Fixture.PostAsync(finance, path + "/reconcile", new { });
        Assert.Equal(HttpStatusCode.OK, wake.StatusCode);
        await PumpRefundsAsync(fixture);
        Assert.Equal(reads + 1, await fixture.CountAsync("refund_observations"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_refunds"));
    }
}
