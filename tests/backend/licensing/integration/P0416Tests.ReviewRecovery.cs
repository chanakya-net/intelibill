using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    [Fact]
    public async Task CommittedShopClosureRecoversAfterLostReplyAndValidationContention()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() }, observeDelegation: true);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        var path = $"/api/licensing/shops/{owner.ShopId:D}/closure";
        var preview = await owner.Client.GetFromJsonAsync<JsonElement>(path + "-preview");
        var command = ConfirmShopCommand(preview);

        string? originalDecision = null;
        fixture.Transport.AfterCommit = async (request, response, ct) =>
        {
            if (request.RequestUri?.AbsolutePath != ShopClosuresV1.ShopPath(owner.ShopId) || !response.IsSuccessStatusCode)
                return;
            originalDecision = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException("Fixture lost the committed reply.");
        };
        using var lost = await owner.Client.PostAsJsonAsync(path, command);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        Assert.NotNull(originalDecision);
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_targets"));
        fixture.Transport.AfterCommit = null;

        await AssertContendedRecoveryAsync(fixture, owner, path, command);
        using var retry = await owner.Client.PostAsJsonAsync(path, command);
        Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        Assert.Equal(originalDecision, await retry.Content.ReadAsStringAsync());
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closures"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }

    private static async Task AssertContendedRecoveryAsync(P0213Fixture fixture, Merchant owner, string path,
        ShopClosureConfirmationV1 command)
    {
        await using var holder = new NpgsqlConnection(fixture.MerchantDb.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        fixture.Transport.BeforeDispatch = async (request, ct) =>
        {
            if (request.RequestUri?.AbsolutePath != ShopClosuresV1.ValidationPath) return;
            await using var sql = new NpgsqlCommand("SELECT public.take_merchant_licence_fence(NULL::uuid, $1, false)",
                holder, transaction);
            sql.Parameters.AddWithValue(owner.ShopId);
            await sql.ExecuteNonQueryAsync(ct);
        };
        var pending = owner.Client.PostAsJsonAsync(path, command);
        await P0403Tests.WaitForBlockedAsync(fixture, holder.ProcessID, 1);
        using var busy = await pending;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        await transaction.CommitAsync();
        fixture.Transport.BeforeDispatch = null;
    }

    [Fact]
    public async Task ClosureKeysAreScopedToTheShopConsentAndTheBillingAccountProposal()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var one = await CreateTradingShopAsync(fixture.Hosts);
        var two = await CreateTradingShopAsync(fixture.Hosts);
        var first = await CreateAccountAsync(one.Client);
        var second = await CreateAccountAsync(two.Client);
        await P0213Fixture.LinkAsync(first, one, one);
        await P0213Fixture.LinkAsync(second, two, two);
        await fixture.DrainAsync(source: true);

        var key = Guid.NewGuid();
        var original = await CreateProposalAsync(one, first, key);
        var other = await CreateProposalAsync(two, second, key);
        Assert.NotEqual(original.GetProperty("id").GetGuid(), other.GetProperty("id").GetGuid());
        Assert.Equal(first, original.GetProperty("accountId").GetGuid());
        Assert.Equal(second, other.GetProperty("accountId").GetGuid());
        Assert.Equal(original.GetRawText(), (await CreateProposalAsync(one, first, key)).GetRawText());
        Assert.Equal(other.GetRawText(), (await CreateProposalAsync(two, second, key)).GetRawText());

        var shop = $"/api/licensing/shops/{one.ShopId:D}";
        var review = await one.Client.GetFromJsonAsync<JsonElement>(shop + "/closure-preview");
        var command = ConfirmShopCommand(review);
        using var consent = await one.Client.PostAsJsonAsync(ConsentPath(shop, original), command);
        Assert.True(consent.IsSuccessStatusCode, await consent.Content.ReadAsStringAsync());
        var saved = await ReadConsentRowsAsync(fixture, one);
        using var replay = await one.Client.PostAsJsonAsync(ConsentPath(shop, original), command);
        Assert.Equal(await consent.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());

        var replacement = await CreateProposalAsync(one, first, Guid.NewGuid());
        using var conflict = await one.Client.PostAsJsonAsync(ConsentPath(shop, replacement), command);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("Billing.OperationPayloadMismatch", await P0206Fixture.CodeAsync(conflict));
        Assert.Equal(saved, await ReadConsentRowsAsync(fixture, one));
        Assert.Equal(3L, await fixture.Trial.CountAsync("operational_closure_proposals"));
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }

    private static async Task<JsonElement> CreateProposalAsync(Merchant owner, Guid account, Guid key)
    {
        using var response = await owner.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/closures",
            new { operationKey = key, financialTreatment = "NoFinancialChange" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string ConsentPath(string shop, JsonElement proposal) =>
        $"{shop}/account-closure/{proposal.GetProperty("id").GetGuid():D}";

    private static Task<object?> ReadConsentRowsAsync(P0213Fixture fixture, Merchant owner) =>
        fixture.MerchantScalarAsync($"""
            SELECT jsonb_agg(to_jsonb(c) ORDER BY c.id)::text FROM public.shop_closure_consents c WHERE shop_id = '{owner.ShopId:D}'
            """);
}
