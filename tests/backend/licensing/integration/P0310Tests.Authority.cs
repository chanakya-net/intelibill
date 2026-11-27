using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Fact]
    public async Task SelectionAuthorizesIntendedCapabilityAndFreshMfaBeforeReplay()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, Settings());
        using var billing = await P0306Tests.SignInBillingAsync(fixture);
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var request = new { profileId = "a", operationKey = Guid.NewGuid(), expectedRevision = 0, reason = "Owner selected A" };
        using (var denied = await P0212Fixture.PostAsync(billing, ProvidersPath + "/selection", request))
            Assert.Equal("PaymentProvider.Denied", await P0206Fixture.CodeAsync(denied));
        using (var noCsrf = await owner.SendAsync(HttpMethod.Post, ProvidersPath + "/selection", request))
            Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using (var foreign = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection", request, "https://foreign.example"))
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using (var selected = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection", request))
            Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        using (var mismatch = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection",
            new { profileId = "b", request.operationKey, expectedRevision = 0, request.reason }))
            Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(mismatch));
        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(10));
        using (var recent = await owner.SendAsync(HttpMethod.Get, ProvidersPath + "/operations/" + request.operationKey))
            Assert.Equal(HttpStatusCode.OK, recent.StatusCode);
        fixture.Trial.Catalogue.Clock.Advance(TimeSpan.FromTicks(10));
        using (var expired = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection", request))
            Assert.Equal("PaymentProvider.RecentMfaRequired", await P0206Fixture.CodeAsync(expired));
        using (var expiredLookup = await owner.SendAsync(HttpMethod.Get, ProvidersPath + "/operations/" + request.operationKey))
            Assert.Equal("PaymentProvider.RecentMfaRequired", await P0206Fixture.CodeAsync(expiredLookup));
        Assert.Equal(1L, await fixture.CountAsync("payment_provider_selections"));
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            UPDATE public.admin_sessions SET revoked_at = '{fixture.Trial.Catalogue.Clock.GetUtcNow():O}'
            WHERE admin_id IN (SELECT id FROM public.admin_identities WHERE lower(email) = lower('{P0112Tests.OwnerEmail}'))
            """);
        Assert.True(await fixture.CountAsync("admin_sessions", "revoked_at IS NOT NULL") > 0);
        using var revoked = await P0212Fixture.PostAsync(owner, ProvidersPath + "/selection", request);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task CompetingSwitchesUseOneRevisionAndUnpreparedReplicaNeverUsesLegacyDefault()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, Settings());
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        using var anotherSession = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var responses = await Task.WhenAll(new[] { (owner, "a"), (anotherSession, "b") }.Select(async pair =>
            await P0212Fixture.PostAsync(pair.Item1, ProvidersPath + "/selection", new
            { profileId = pair.Item2, operationKey = Guid.NewGuid(), expectedRevision = 0, reason = "Concurrent provider switch" })));
        foreach (var response in responses) response.Dispose();
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1L, await fixture.CountAsync("payment_provider_selections"));
        using var read = await owner.SendAsync(HttpMethod.Get, ProvidersPath);
        var current = (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("selection");
        var profile = current.GetProperty("profileId").GetString();
        var unavailable = fixture.Hosts.Licensing.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        unavailable[$"Payments:Profiles:{(profile == "a" ? 0 : 1)}:ConfigurationVersion"] = "not-prepared";
        using var scope = fixture.Hosts.Licensing.Services.CreateScope();
        var selection = await scope.ServiceProvider.GetRequiredService<IPaymentProviderSelection>().SelectedAsync(false, default);
        Assert.Equal(1, selection.Revision);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IPaymentGatewayResolver>().ForPin(selection.Pin!));
        var purchase = await fixture.AcceptedQuoteAsync(1);
        Assert.False((await StatusAsync(purchase.Payer.Client, purchase.Quote)).GetProperty("canStart").GetBoolean());
        using var refused = await purchase.Payer.Client.PostAsJsonAsync(AttemptsPath(purchase.Quote), Start(purchase.Quote));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("development_gateway_sessions"));
    }
}
