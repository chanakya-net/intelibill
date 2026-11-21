using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    [Fact]
    public async Task AccountClosureRequiresEveryCurrentOwner()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var payer = await CreateTradingShopAsync(fixture.Hosts);
        var shopOwner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        await P0213Fixture.LinkAsync(account, payer, payer);
        await P0213Fixture.LinkAsync(account, payer, shopOwner);
        await fixture.DrainAsync(source: true);

        var path = $"/api/billing/accounts/{account:D}/closures";
        using var absent = await payer.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        using var proposed = await payer.Client.PostAsJsonAsync(path,
            new { operationKey = Guid.NewGuid(), financialTreatment = "NoFinancialChange" });
        Assert.True(proposed.IsSuccessStatusCode, await proposed.Content.ReadAsStringAsync());
        var proposal = await proposed.Content.ReadFromJsonAsync<JsonElement>();
        var id = proposal.GetProperty("id").GetGuid();
        var digest = proposal.GetProperty("scopeDigest").GetString();
        Assert.Equal(2, proposal.GetProperty("shops").GetArrayLength());
        var confirmation = new { operationKey = Guid.NewGuid(), scopeDigest = digest, confirmed = true };
        using var missing = await payer.Client.PostAsJsonAsync($"{path}/{id:D}/confirm", confirmation);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_closures"));

        await ConsentAsync(payer, id);
        await ConsentAsync(shopOwner, id);
        using var denied = await shopOwner.Client.PostAsJsonAsync($"{path}/{id:D}/confirm", confirmation);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var saved = await payer.Client.PostAsJsonAsync($"{path}/{id:D}/confirm", confirmation);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        Assert.Equal("Closed", (await fixture.LatestSnapshotAsync(payer.ShopId)).Shop?.State);
        Assert.Equal("Closed", (await fixture.LatestSnapshotAsync(shopOwner.ShopId)).Shop?.State);

        var decision = await saved.Content.ReadFromJsonAsync<JsonElement>();
        var state = await payer.Client.GetFromJsonAsync<JsonElement>(path);
        var closed = state.GetProperty("closed");
        Assert.Equal(decision.GetProperty("savedAt").GetDateTimeOffset(), closed.GetProperty("savedAt").GetDateTimeOffset());
        Assert.Equal(2, closed.GetProperty("shops").GetArrayLength());
        using var repeat = await payer.Client.PostAsJsonAsync(path,
            new { operationKey = Guid.NewGuid(), financialTreatment = "NoFinancialChange" });
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal("Closure.AccountClosed", await P0206Fixture.CodeAsync(repeat));
    }

    [Fact]
    public async Task BillingOwnershipTransferInvalidatesThePendingProposal()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var original = await CreateTradingShopAsync(fixture.Hosts);
        var successor = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(original.Client);
        await P0213Fixture.LinkAsync(account, original, original);
        await fixture.DrainAsync(source: true);
        var proposal = await ProposeAsync(original, account);
        await ConsentAsync(original, proposal.GetProperty("id").GetGuid());
        await TransferBillingOwnershipAsync(original, successor, account);

        using var denied = await ConfirmAccountAsync(original, account, proposal);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var stale = await ConfirmAccountAsync(successor, account, proposal);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_closures"));
        var replacement = await ProposeAsync(successor, account);
        await ConsentAsync(original, replacement.GetProperty("id").GetGuid());
        using var confirmed = await ConfirmAccountAsync(successor, account, replacement);
        Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync());
        Assert.Equal("Closed", (await fixture.LatestSnapshotAsync(original.ShopId)).Shop?.State);
    }

    [Fact]
    public async Task AShopOnlyAcknowledgementCannotAuthorizeAccountClosure()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() }, observeDelegation: true);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        var shopPath = $"/api/licensing/shops/{owner.ShopId:D}";
        var preview = await owner.Client.GetFromJsonAsync<JsonElement>(shopPath + "/closure-preview");
        var shopOnly = ConfirmShopCommand(preview);
        fixture.Transport.BeforeDispatch = (request, _) =>
            request.RequestUri?.AbsolutePath == ShopClosuresV1.ShopPath(owner.ShopId)
                ? Task.FromException(new HttpRequestException("Fixture delegation outage.")) : Task.CompletedTask;
        using var unconfirmed = await owner.Client.PostAsJsonAsync(shopPath + "/closure", shopOnly);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfirmed.StatusCode);
        fixture.Transport.BeforeDispatch = null;

        using var response = await owner.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/closures",
            new { operationKey = shopOnly.OperationKey, financialTreatment = "NoFinancialChange" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var proposal = await response.Content.ReadFromJsonAsync<JsonElement>();
        using var refused = await ConfirmAccountAsync(owner, account, proposal);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_closures"));

        await ConsentAsync(owner, proposal.GetProperty("id").GetGuid());
        using var confirmed = await ConfirmAccountAsync(owner, account, proposal);
        Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync());
    }

    private static async Task TransferBillingOwnershipAsync(Merchant original, Merchant successor, Guid account)
    {
        using var offered = await original.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/ownership-transfers", new
        {
            operationKey = Guid.NewGuid(), expectedVersion = 1, recipientKind = "email", recipient = successor.Email,
            reason = "Responsibility handover", confirmTransfer = true,
        });
        Assert.Equal(HttpStatusCode.Accepted, offered.StatusCode);
        var inbox = await successor.Client.GetFromJsonAsync<JsonElement>("/api/billing/ownership-transfers");
        var transfer = Assert.Single(inbox.GetProperty("transfers").EnumerateArray()).GetProperty("transferId").GetGuid();
        using var accepted = await successor.Client.PostAsJsonAsync($"/api/billing/ownership-transfers/{transfer:D}/accept", new
        {
            operationKey = Guid.NewGuid(), expectedVersion = 1, responsibilityPolicyVersion = "prospective-billing-owner/1",
            acceptResponsibility = true,
        });
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
    }

    private static async Task ConsentAsync(Merchant owner, Guid proposal)
    {
        var path = $"/api/licensing/shops/{owner.ShopId:D}";
        var preview = await owner.Client.GetFromJsonAsync<JsonElement>(path + "/closure-preview");
        var pending = await owner.Client.GetFromJsonAsync<JsonElement>(path + "/account-closure");
        Assert.Equal(proposal, pending.GetProperty("id").GetGuid());
        using var consent = await owner.Client.PostAsJsonAsync($"{path}/account-closure/{proposal:D}", new
        {
            operationKey = Guid.NewGuid(),
            expectedVersion = preview.GetProperty("closureVersion").GetInt64(),
            ownerVersion = preview.GetProperty("ownerVersion").GetInt64(),
            workDigest = preview.GetProperty("workDigest").GetString(),
            reviewedKnownWork = true, consideredDeviceQueues = true,
            financialTreatment = "NoFinancialChange",
        });
        Assert.True(consent.IsSuccessStatusCode, await consent.Content.ReadAsStringAsync());
    }
}
