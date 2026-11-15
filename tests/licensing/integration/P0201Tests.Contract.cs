using System.Net;
using System.Text.Json;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0201Tests
{
    [Fact]
    public async Task ContractLifetimeAndFailures_MatchLicensingResponses()
    {
        using var contract = await ReadDelegationContractAsync();
        var lifetime = contract.RootElement.GetProperty("lifetime");
        var failures = contract.RootElement.GetProperty("internalFailures");
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = fixture.LicensingApi();
        using var client = licensing.CreateClient();
        var account = Guid.NewGuid();
        var actor = Guid.NewGuid();
        await fixture.GrantAsync(account, actor);

        foreach (var boundary in lifetime.GetProperty("validSeconds").EnumerateArray())
        {
            var assertion = CreateContractAssertion(fixture, account, actor, lifetime, boundary);
            using var response = await P0201Fixture.SendAssertionAsync(client, account, assertion.Request, assertion.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var replay = await P0201Fixture.SendAssertionAsync(client, account, assertion.Request, assertion.Token);
            await AssertContractFailureAsync(replay, failures, "replay");
        }

        foreach (var boundary in lifetime.GetProperty("invalidSeconds").EnumerateArray())
        {
            var assertion = CreateContractAssertion(fixture, account, actor, lifetime, boundary);
            using var response = await P0201Fixture.SendAssertionAsync(client, account, assertion.Request, assertion.Token);
            await AssertContractFailureAsync(response, failures, "invalid");
        }

        fixture.Clock.Now = DateTimeOffset.FromUnixTimeSeconds(lifetime.GetProperty("issuedAt").GetInt64());
        await AssertContractMembershipFailuresAsync(fixture, client, account, actor, failures);
        Assert.Equal((long)lifetime.GetProperty("validSeconds").GetArrayLength(), await ReplayCountAsync(fixture));
    }

    private static async Task AssertContractMembershipFailuresAsync(
        P0201Fixture fixture, HttpClient client, Guid account, Guid actor, JsonElement failures)
    {
        var outsider = CreateAssertion(fixture, account, Guid.NewGuid());
        using var denied = await P0201Fixture.SendAssertionAsync(client, account, outsider.Request, outsider.Token);
        await AssertContractFailureAsync(denied, failures, "denied");

        await using var missingMembership = fixture.LicensingApi(membership: false);
        using var missingClient = missingMembership.CreateClient();
        var member = CreateAssertion(fixture, account, actor);
        using var unavailable = await P0201Fixture.SendAssertionAsync(missingClient, account, member.Request, member.Token);
        await AssertContractFailureAsync(unavailable, failures, "membershipUnavailable");
    }

    private static LocalAssertion CreateContractAssertion(
        P0201Fixture fixture, Guid account, Guid actor, JsonElement lifetime, JsonElement boundary)
    {
        var issuedAt = lifetime.GetProperty("issuedAt").GetInt64();
        var expiresAt = lifetime.GetProperty("expiresAt").GetInt64();
        Assert.Equal(lifetime.GetProperty("maxSeconds").GetInt64(), expiresAt - issuedAt);
        fixture.Clock.Now = DateTimeOffset.FromUnixTimeSeconds(boundary.GetInt64());
        return CreateAssertion(fixture, account, actor, claims =>
        {
            claims["iat"] = issuedAt;
            claims["nbf"] = lifetime.GetProperty("notBefore").GetInt64();
            claims["exp"] = expiresAt;
        });
    }

    private static Task AssertContractFailureAsync(HttpResponseMessage response, JsonElement failures, string name)
    {
        var failure = failures.GetProperty(name);
        return AssertProblemAsync(response,
            (HttpStatusCode)failure.GetProperty("status").GetInt32(), failure.GetProperty("code").GetString()!);
    }
}
