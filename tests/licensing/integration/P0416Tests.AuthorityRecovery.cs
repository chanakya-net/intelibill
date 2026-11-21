using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShopRecoveryAcrossOwnerRevisionRequiresACommittedDecision(bool committed)
    {
        await using var fixture = await RecoveryFixtureAsync();
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        var path = $"/api/licensing/shops/{owner.ShopId:D}/closure";
        var command = ConfirmShopCommand(await owner.Client.GetFromJsonAsync<JsonElement>(path + "-preview"));
        var delivery = ShopClosuresV1.ShopPath(owner.ShopId);
        if (committed) LoseReply(fixture, delivery);
        else InterruptDelivery(fixture, delivery);
        using var lost = await owner.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        fixture.Transport.AfterCommit = null;
        fixture.Transport.BeforeDispatch = null;
        await fixture.MerchantScalarAsync($"""
            UPDATE public.shop_memberships SET authority_version = authority_version + 1
            WHERE shop_id = '{owner.ShopId:D}' AND user_id = '{owner.UserId:D}' RETURNING authority_version
            """);
        var current = await owner.Client.GetFromJsonAsync<JsonElement>(path + "-preview");
        Assert.True(current.GetProperty("ownerVersion").GetInt64() > command.OwnerVersion);
        using var changed = await owner.Client.PostAsJsonAsync(path, command with { OwnerVersion = current.GetProperty("ownerVersion").GetInt64() });
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(changed));
        using var replay = await owner.Client.PostAsJsonAsync(path, command);
        Assert.Equal(committed ? 1L : 0L, await fixture.Trial.CountAsync("operational_closures"));
        if (committed)
        {
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            Assert.Equal(command.OperationKey, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operationKey").GetGuid());
        }
        else
        {
            Assert.Equal(ShopClosuresV1.ReviewChangedCode, await P0206Fixture.CodeAsync(replay));
            using var fresh = await owner.Client.PostAsJsonAsync(path, ConfirmShopCommand(current));
            Assert.True(fresh.IsSuccessStatusCode, await fresh.Content.ReadAsStringAsync());
        }
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }

    [Fact]
    public async Task SavedOwnerNoticeRecoversAfterBillingOwnerFinalizes()
    {
        await using var fixture = await RecoveryFixtureAsync();
        var payer = await CreateTradingShopAsync(fixture.Hosts);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        await P0213Fixture.LinkAsync(account, payer, owner);
        await fixture.DrainAsync(source: true);
        var proposal = await ProposeAsync(payer, account);
        var shop = $"/api/licensing/shops/{owner.ShopId:D}";
        var path = ConsentPath(shop, proposal);
        var command = ConfirmShopCommand(await owner.Client.GetFromJsonAsync<JsonElement>(shop + "/closure-preview"));
        var notice = ShopClosuresV1.NoticePath(owner.ShopId);
        LoseReply(fixture, notice);
        using var lost = await owner.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        fixture.Transport.AfterCommit = null;
        var unused = command with { OperationKey = Guid.NewGuid() };
        var used = command with { OperationKey = Guid.NewGuid() };
        InterruptDelivery(fixture, notice);
        foreach (var consent in new[] { unused, used })
        {
            using var unavailable = await owner.Client.PostAsJsonAsync(path, consent);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        }
        fixture.Transport.BeforeDispatch = null;
        using var saved = await ConfirmAccountAsync(payer, account, proposal);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        await AssertUsedConsentAsync(fixture, owner, account, used.OperationKey);
        await fixture.DrainAsync(source: true);
        using var hidden = await owner.Client.GetAsync(shop + "/account-closure");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        foreach (var consent in new[] { command, used })
        {
            using var replay = await owner.Client.PostAsJsonAsync(path, consent);
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("consentRecorded").GetBoolean());
        }
        using var absent = await owner.Client.PostAsJsonAsync(path, unused);
        Assert.Equal(HttpStatusCode.Conflict, absent.StatusCode);
        Assert.Equal(ShopClosuresV1.ReviewChangedCode, await P0206Fixture.CodeAsync(absent));
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_consent_notices"));

        // A legacy shop-level acknowledgement cannot prove which request arrived.
        await fixture.SourceScalarAsync($"""
            INSERT INTO public.operational_closure_consent_notices (proposal_id, account_id, shop_id)
            VALUES ('{proposal.GetProperty("id").GetGuid():D}', '{account:D}', '{owner.ShopId:D}') RETURNING shop_id
            """);
        using var ambiguous = await owner.Client.PostAsJsonAsync(path, unused);
        Assert.Equal(HttpStatusCode.NotFound, ambiguous.StatusCode);
        Assert.Equal(2L, await fixture.Trial.CountAsync("operational_closure_consent_notices"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closures"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }

    private static async Task AssertUsedConsentAsync(P0213Fixture fixture, Merchant owner, Guid account, Guid operation)
    {
        var consentId = await fixture.MerchantScalarAsync($"""
            SELECT id FROM public.shop_closure_consents WHERE shop_id = '{owner.ShopId:D}' AND operation_key = '{operation:D}'
            """);
        Assert.IsType<Guid>(consentId);
        Assert.Equal(consentId, await fixture.SourceScalarAsync($"""
            SELECT consent_id FROM public.operational_closure_targets WHERE account_id = '{account:D}' AND shop_id = '{owner.ShopId:D}'
            """));
    }

    [Fact]
    public async Task ProposalKeyStaysBoundToTheOriginalBillingOwnerRevision()
    {
        await using var fixture = await RecoveryFixtureAsync();
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var successor = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        var path = $"/api/billing/accounts/{account:D}/closures";
        var command = new { operationKey = Guid.NewGuid(), financialTreatment = "NoFinancialChange" };
        LoseReply(fixture, ShopClosuresV1.AccountPath(account));
        using var lost = await owner.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        fixture.Transport.AfterCommit = null;
        await TransferBillingOwnershipAsync(owner, successor, account);
        await TransferBillingOwnershipAsync(successor, owner, account);
        using var replay = await owner.Client.PostAsJsonAsync(path, command);
        Assert.Equal(ShopClosuresV1.ReviewChangedCode, await P0206Fixture.CodeAsync(replay));
        using var current = await owner.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, current.StatusCode);
        var fresh = await CreateProposalAsync(owner, account, Guid.NewGuid());
        Assert.NotEqual(command.operationKey, fresh.GetProperty("operationKey").GetGuid());
        Assert.Equal(2L, await fixture.Trial.CountAsync("operational_closure_proposals"));
    }

    private Task<P0213Fixture> RecoveryFixtureAsync() => P0213Fixture.StartAsync(database,
        businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() }, observeDelegation: true);

    private static void InterruptDelivery(P0213Fixture fixture, string path) =>
        fixture.Transport.BeforeDispatch = (request, _) => request.RequestUri?.AbsolutePath == path
            ? Task.FromException(new HttpRequestException("Fixture interrupted delivery.")) : Task.CompletedTask;

    private static void LoseReply(P0213Fixture fixture, string path) =>
        fixture.Transport.AfterCommit = (request, response, _) => request.RequestUri?.AbsolutePath == path && response.IsSuccessStatusCode
            ? Task.FromException(new HttpRequestException("Fixture lost the committed reply.")) : Task.CompletedTask;
}
