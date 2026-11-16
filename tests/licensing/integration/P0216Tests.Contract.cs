using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0216Tests
{
    [Fact]
    public void WithdrawalContractPinsPathsActionsAndCompatibleStatus()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/subscription-cancellation-v1.json")));
        var root = contract.RootElement.GetProperty("withdrawal");
        var actions = root.GetProperty("actions");
        Assert.Equal(SubscriptionCancellationsV1.Withdraw, actions.GetProperty("withdraw").GetString());
        Assert.Equal(SubscriptionCancellationsV1.WithdrawRecover, actions.GetProperty("recover").GetString());
        Assert.True(actions.EnumerateObject().All(action => SubscriptionCancellationsV1.IsAction(action.Value.GetString()!)));

        var account = Guid.NewGuid();
        var term = Guid.NewGuid();
        var operation = Guid.NewGuid();
        string Bind(string name) => root.GetProperty(name).GetString()!
            .Replace("{accountId}", account.ToString("D"), StringComparison.Ordinal)
            .Replace("{termId}", term.ToString("D"), StringComparison.Ordinal)
            .Replace("{operationKey}", operation.ToString("D"), StringComparison.Ordinal);
        Assert.Equal(Bind("internalPath"), SubscriptionCancellationsV1.WithdrawalPath(account, term));
        Assert.Equal(Bind("recoveryPath"), SubscriptionCancellationsV1.WithdrawalOperationPath(account, term, operation));
        Assert.Equal(Bind("publicPath"), WithdrawalPath(account, term));

        var example = root.GetProperty("requestExample");
        var request = example.Deserialize<WithdrawSubscriptionCancellationV1>(ContractJson)!;
        Assert.True(request.IsValid());
        Assert.True(JsonElement.DeepEquals(example, JsonSerializer.SerializeToElement(request, ContractJson)));
        Assert.False((request with { CurrentVersion = 2 }).IsValid());
        Assert.False((request with { Confirmed = false }).IsValid());
        var outcome = root.GetProperty("outcomeExample");
        Assert.True(JsonElement.DeepEquals(outcome, JsonSerializer.SerializeToElement(
            outcome.Deserialize<SubscriptionCancellationWithdrawalOutcomeV1>(ContractJson), ContractJson)));
        Assert.Equal("Billing.CancellationEffective", root.GetProperty("refusals").GetProperty("effective").GetString());
        Assert.All(new long[] { 1, 3, 5 }, version => Assert.True(SubscriptionCancellationsV1.IsScheduledVersion(version)));
        Assert.All(new long[] { 0, 2, 4 }, version => Assert.True(SubscriptionCancellationsV1.IsOpenVersion(version)));
        Assert.All(new long[] { -1, 0, 2 }, version => Assert.False(SubscriptionCancellationsV1.IsScheduledVersion(version)));
    }

    [Fact]
    public async Task StatusRelayAcceptsOnlyOddCancellationVersions()
    {
        var transport = new EvenCancellation();
        await using var host = await StartAsync(database, P0210Fixture.Start, inner => { transport.InnerHandler = inner; return transport; });
        var owner = await SignInAsync(host, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await SeedPaidAsync(host, owner, account, owner);
        transport.Path = SubscriptionStatusV1.ShopPath(owner.ShopId);
        transport.Version = 3;
        using (var odd = await ShopStatusAsync(owner.Client, owner.ShopId))
            Assert.Equal(3L, (await ReadAsync(odd)).GetProperty("term").GetProperty("cancellation").GetProperty("version").GetInt64());
        transport.Version = 2;
        using var rejected = await ShopStatusAsync(owner.Client, owner.ShopId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
    }

    private sealed class EvenCancellation : DelegatingHandler
    {
        internal string? Path { get; set; }
        internal long Version { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.RequestUri?.AbsolutePath != Path || !response.IsSuccessStatusCode) return response;
            var status = (await response.Content.ReadFromJsonAsync<ShopSubscriptionStatusV1>(ct))!;
            response.Content.Dispose();
            response.Content = JsonContent.Create(status with
            {
                Term = status.Term! with
                {
                    GraceEndsAt = null, AccessEndsAt = status.Term.PaidThrough,
                    Cancellation = new(Guid.NewGuid(), Version, status.Term.PaidThrough, SubscriptionCancellationsV1.Policy),
                },
            });
            return response;
        }
    }
}
