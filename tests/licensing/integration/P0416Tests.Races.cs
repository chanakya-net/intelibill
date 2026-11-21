using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0416Tests
{
    [Fact]
    public async Task AccountConfirmationWaitsForAndRechecksMerchantOwnerVersion()
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var payer = await CreateTradingShopAsync(fixture.Hosts);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        await P0213Fixture.LinkAsync(account, payer, payer);
        await P0213Fixture.LinkAsync(account, payer, owner);
        await fixture.DrainAsync(source: true);
        var proposal = await ProposeAsync(payer, account);
        var id = proposal.GetProperty("id").GetGuid();
        await ConsentAsync(payer, id);
        await ConsentAsync(owner, id);

        await using var holder = new NpgsqlConnection(fixture.MerchantDb.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using var bump = new NpgsqlCommand("""
            UPDATE public.shop_memberships SET authority_version = authority_version + 1 WHERE shop_id = $1 AND user_id = $2
            """, holder, transaction);
        bump.Parameters.AddWithValue(owner.ShopId);
        bump.Parameters.AddWithValue(owner.UserId);
        Assert.Equal(1, await bump.ExecuteNonQueryAsync());

        var raced = ConfirmAccountAsync(payer, account, proposal);
        await P0403Tests.WaitForBlockedAsync(fixture, holder.ProcessID, 1);
        await transaction.CommitAsync();
        using var stale = await raced;
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(0L, await fixture.Trial.CountAsync("operational_closure_targets"));

        await ConsentAsync(owner, id);
        using var saved = await ConfirmAccountAsync(payer, account, proposal);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        Assert.Equal(2L, await fixture.Trial.CountAsync("operational_closure_targets"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AffiliationAndAccountClosureRespectTheAccountFence(bool affiliationFirst)
    {
        await using var fixture = await P0213Fixture.StartAsync(database,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() }, observeDelegation: !affiliationFirst);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var incoming = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        await fixture.DrainAsync(source: true);
        var proposal = await ProposeAsync(owner, account);
        await ConsentAsync(owner, proposal.GetProperty("id").GetGuid());
        var pending = await RequestAffiliationAsync(owner, incoming, account);
        var consentPath = $"/api/billing/accounts/{account:D}/affiliations/{incoming.ShopId:D}/{pending:D}/consent";
        Assert.Equal("Granted", await P0205Tests.DecideAsync(incoming.Client, consentPath, "grant"));

        if (affiliationFirst)
        {
            await CompleteAffiliationAsync(incoming, consentPath, "Applied");
            using var stale = await ConfirmAccountAsync(owner, account, proposal);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(0L, await fixture.Trial.CountAsync("operational_closure_targets"));
            return;
        }

        await ConfirmWhileAffiliationWaitsAsync(fixture, owner, incoming, account, proposal, consentPath);
        Assert.Equal(1L, await fixture.Trial.CountAsync("operational_closure_targets"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("shop_affiliations"));
    }

    private static async Task ConfirmWhileAffiliationWaitsAsync(P0213Fixture fixture, Merchant owner,
        Merchant incoming, Guid account, JsonElement proposal, string consentPath)
    {
        var validated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var affiliationReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliverAffiliation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.BeforeDispatch = async (request, token) =>
        {
            if (request.RequestUri?.AbsolutePath != ShopAffiliationsV1.DeliveryPath) return;
            affiliationReady.TrySetResult();
            await deliverAffiliation.Task.WaitAsync(token);
        };
        fixture.Transport.AfterCommit = async (request, response, token) =>
        {
            if (request.RequestUri?.AbsolutePath == ShopClosuresV1.ValidationPath &&
                response.IsSuccessStatusCode)
            {
                validated.TrySetResult();
                await resume.Task.WaitAsync(token);
            }
        };
        try
        {
            Assert.Equal("Accepted", await P0205Tests.DecideAsync(incoming.Client, consentPath, "accept"));
            await affiliationReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var confirming = ConfirmAccountAsync(owner, account, proposal);
            await validated.Task.WaitAsync(TimeSpan.FromSeconds(3));
            deliverAffiliation.TrySetResult();
            Assert.True(await AffiliationIsBlockedAsync(fixture));
            resume.TrySetResult();
            using var closure = await confirming;
            Assert.True(closure.IsSuccessStatusCode, await closure.Content.ReadAsStringAsync());
            await P0205Tests.WaitForConsentAsync(incoming.Client, consentPath, "NeedsReview");
        }
        finally
        {
            resume.TrySetResult();
            deliverAffiliation.TrySetResult();
        }
    }

    private static async Task<bool> AffiliationIsBlockedAsync(P0213Fixture fixture)
    {
        for (var attempt = 0; attempt < 55; attempt++)
        {
            var waiting = await fixture.SourceScalarAsync("""
                SELECT EXISTS (SELECT FROM pg_stat_activity WHERE datname = current_database()
                    AND cardinality(pg_blocking_pids(pid)) > 0 AND query LIKE '%pg_advisory_xact_lock(hashtextextended%')
                """);
            if (waiting is true) return true;
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
        return false;
    }

    private static async Task<JsonElement> ProposeAsync(Merchant payer, Guid account)
    {
        using var response = await payer.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/closures",
            new { operationKey = Guid.NewGuid(), financialTreatment = "NoFinancialChange" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> ConfirmAccountAsync(Merchant payer, Guid account, JsonElement proposal)
    {
        var id = proposal.GetProperty("id").GetGuid();
        return payer.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/closures/{id:D}/confirm",
            new { operationKey = Guid.NewGuid(), scopeDigest = proposal.GetProperty("scopeDigest").GetString(), confirmed = true });
    }

    private static async Task<Guid> RequestAffiliationAsync(Merchant payer, Merchant incoming, Guid account)
    {
        using var response = await payer.Client.PostAsJsonAsync($"/api/billing/accounts/{account:D}/affiliations",
            new { operationKey = Guid.NewGuid(), shopId = incoming.ShopId, withoutCoverage = true, termId = (Guid?)null });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("requestId").GetGuid();
    }

    private static async Task CompleteAffiliationAsync(Merchant incoming, string path, string disposition)
    {
        Assert.Contains(await P0205Tests.DecideAsync(incoming.Client, path, "accept"), new[] { "Accepted", disposition });
        await P0205Tests.WaitForConsentAsync(incoming.Client, path, disposition);
    }
}
