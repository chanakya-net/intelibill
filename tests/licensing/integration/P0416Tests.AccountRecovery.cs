using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccountConfirmationRecoversOnlyCommittedWorkAfterOwnershipReturns(bool committed)
    {
        await using var fixture = await RecoveryFixtureAsync();
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var successor = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        var proposal = await ProposeAsync(owner, account);
        var id = proposal.GetProperty("id").GetGuid();
        await ConsentAsync(owner, id);

        var command = new ConfirmAccountClosureV1(Guid.NewGuid(), proposal.GetProperty("scopeDigest").GetString()!, true);
        var path = $"/api/billing/accounts/{account:D}/closures/{id:D}/confirm";
        var destination = ShopClosuresV1.ConfirmPath(account, id);
        string? receipt = null;
        if (committed)
            fixture.Transport.AfterCommit = async (request, response, ct) =>
            {
                if (request.RequestUri?.AbsolutePath != destination || !response.IsSuccessStatusCode) return;
                receipt = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException("Fixture lost the committed reply.");
            };
        else
            InterruptDelivery(fixture, destination);
        using var lost = await owner.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        Assert.Equal(committed, receipt is not null);
        fixture.Transport.BeforeDispatch = null;
        fixture.Transport.AfterCommit = null;

        await TransferBillingOwnershipAsync(owner, successor, account);
        using var denied = await owner.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var otherActor = await successor.Client.PostAsJsonAsync(path, command);
        Assert.Equal(ShopClosuresV1.ReviewChangedCode, await P0206Fixture.CodeAsync(otherActor));
        await TransferBillingOwnershipAsync(successor, owner, account);
        using var changed = await owner.Client.PostAsJsonAsync(path, command with { ScopeDigest = new string('a', 64) });
        Assert.Equal(ShopClosuresV1.ReviewChangedCode, await P0206Fixture.CodeAsync(changed));
        using var replay = await owner.Client.PostAsJsonAsync(path, command);
        if (committed)
        {
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            Assert.Equal(receipt, await replay.Content.ReadAsStringAsync());
        }
        else
            Assert.Equal(ShopClosuresV1.ReviewChangedCode, await P0206Fixture.CodeAsync(replay));
        Assert.Equal(committed ? 1L : 0L, await fixture.Trial.CountAsync("operational_closures"));
        Assert.Equal(committed ? 1L : 0L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }
}
