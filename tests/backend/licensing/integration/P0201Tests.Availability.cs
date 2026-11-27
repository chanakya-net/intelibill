using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0201Tests
{
    [Fact]
    public async Task InvalidVaultReference_DoesNotSendADelegation()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var loginHost = fixture.MerchantApi();
        using var loginClient = loginHost.CreateClient();
        await SeedMerchantAsync(loginHost.Services);
        var authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(loginClient));
        const string key = "https://billing-vault.test/keys/billing";

        await AssertInvalidVaultReferenceAsync(fixture, authorization, "http://billing-vault.test/keys/billing/pinned", null);
        await AssertInvalidVaultReferenceAsync(fixture, authorization, "not a key URI", null);
        await AssertInvalidVaultReferenceAsync(fixture, authorization, key + "/pinned", key);
        await AssertInvalidVaultReferenceAsync(fixture, authorization, key + "/pinned", "https://billing-vault.test/keys/BILLING/");
        await AssertInvalidVaultReferenceAsync(fixture, authorization, key + "/pinned", key + "/other");
        Assert.Equal(0L, await ReplayCountAsync(fixture));
    }

    private static async Task AssertInvalidVaultReferenceAsync(P0201Fixture fixture, AuthenticationHeaderValue authorization,
        string reference, string? ordinaryKey)
    {
        using var transport = new LocalTransport(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var logs = new P0107Tests.CapturedLogs();
        await using var merchant = new MerchantStartupProbeFactory(fixture.MerchantRuntime, settings:
            new Dictionary<string, string?>
            {
                ["MerchantBillingDelegation:LicensingOrigin"] = "https://licensing.test",
                ["MerchantBillingDelegation:KeyVaultKeyId"] = reference,
                ["Jwt:KeyVaultKeyId"] = ordinaryKey,
            }, configure: services =>
            {
                services.AddHttpClient("MerchantBillingDelegation").ConfigurePrimaryHttpMessageHandler(() => transport);
                services.AddSingleton<ILogEventSink>(logs);
            });
        using var client = merchant.CreateClient();
        client.DefaultRequestHeaders.Authorization = authorization;

        await AssertUnavailableAsync(client);
        Assert.Equal(0, transport.Calls);
        await logs.WaitForAsync("SigningKeyUnavailable");
        Assert.DoesNotContain(logs.Events, entry => entry.Contains(reference, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MembershipStorageFailure_IsUnavailableAndDoesNotConsumeAnAssertion()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        fixture.Clock.Now = DateTimeOffset.UtcNow;
        var logs = new P0107Tests.CapturedLogs();
        await using var licensing = fixture.LicensingApi(
            configure: services => services.AddSingleton<ILogEventSink>(logs));
        using var internalClient = licensing.CreateClient();
        using var transport = new CapturingTransport(licensing.Server.CreateHandler());
        await using var merchant = fixture.MerchantApi(handler: () => transport,
            configure: services => services.AddSingleton<TimeProvider>(fixture.Clock));
        using var client = merchant.CreateClient();
        var actor = await SeedMerchantAsync(merchant.Services);
        var account = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        // The test-only adapter still queries PostgreSQL and runs under the restricted runtime role.
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Owner,
            "REVOKE SELECT ON billing_fixture.memberships FROM licensing_runtime");
        var assertion = CreateAssertion(fixture, account, actor);
        using var direct = await P0201Fixture.SendAssertionAsync(internalClient, account, assertion.Request, assertion.Token);
        await AssertProblemAsync(direct, HttpStatusCode.ServiceUnavailable, "Billing.DelegationUnavailable");
        using var response = await client.GetAsync(MerchantPath(account));
        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "Billing.DelegationUnavailable");
        Assert.Equal(1, transport.Calls);
        Assert.Equal(0L, await ReplayCountAsync(fixture));

        await logs.WaitForAsync("StorageUnavailable");
        Assert.Contains(logs.Events, entry => entry.Contains("StorageUnavailable", StringComparison.Ordinal) &&
            entry.Contains("PostgresException", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Events, entry => entry.Contains(assertion.Token, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Events, entry => entry.Contains(transport.DelegationToken!, StringComparison.Ordinal));

        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Owner,
            "GRANT SELECT ON billing_fixture.memberships TO licensing_runtime");
        using var recovered = await P0201Fixture.SendAssertionAsync(internalClient, account, assertion.Request, assertion.Token);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(1L, await ReplayCountAsync(fixture));
    }
}
