using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    private const string ProvidersPath = "/api/admin/payment-providers";

    [Fact]
    public async Task AuthorizedSwitchChangesOnlyNewAttemptsUnderConcurrency()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, Settings());
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var initial = new { profileId = "a", operationKey = Guid.NewGuid(), expectedRevision = 0, reason = "Select fixture A" };
        using var selected = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection", initial);
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        var first = await fixture.AcceptedQuoteAsync();
        var key = Guid.NewGuid();
        var started = await StartAsync(first.Payer.Client, first.Quote, HttpStatusCode.Created, key);
        var attempt = started.GetProperty("attemptId").GetGuid();
        var change = new { profileId = "b", operationKey = Guid.NewGuid(), expectedRevision = 1, reason = "Switch future attempts" };
        using var switched = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection", change);
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        using var replayedSwitch = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection", change);
        Assert.Equal(await switched.Content.ReadAsStringAsync(), await replayedSwitch.Content.ReadAsStringAsync());
        var replayed = await StartAsync(first.Payer.Client, first.Quote, HttpStatusCode.OK, key);
        Assert.Equal(attempt, replayed.GetProperty("attemptId").GetGuid());
        var second = await fixture.AcceptedQuoteAsync();
        await StartAsync(second.Payer.Client, second.Quote, HttpStatusCode.Created);
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", "provider_account_id = 'simulator-a'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", "provider_account_id = 'simulator-b'"));
        await CaptureAsync(first.Payer.Client, first.Quote, attempt);
        AssertStatus(await VerifyAsync(first.Payer.Client, first.Quote, attempt), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", "provider_account_id = 'simulator-a'"));
        Assert.Equal(2L, await fixture.CountAsync("payment_provider_selections"));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown-provider")]
    [InlineData("duplicate-pin")]
    [InlineData("missing-credential")]
    public async Task InvalidRetainedProfilesNeverResolve(string invalid)
    {
        var settings = Settings();
        if (invalid == "duplicate") settings["Payments:Profiles:1:Id"] = "a";
        if (invalid == "duplicate-pin")
        {
            settings["Payments:Profiles:1:AccountId"] = A.ProviderAccountId;
            settings["Payments:Profiles:1:ConfigurationVersion"] = A.ConfigurationVersion;
        }
        if (invalid == "unknown-provider") settings["Payments:Profiles:0:ProviderId"] = "unknown";
        if (invalid == "missing-credential") settings["Payments:Profiles:0:WebhookSecretReference"] = "Missing:Secret";
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, settings);
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(fixture.Hosts.Licensing.Services);
        var resolver = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<
            Intelibill.Licensing.Application.Payments.IPaymentGatewayResolver>(scope.ServiceProvider);
        Assert.Null(resolver.ForPin(A));
    }
}
