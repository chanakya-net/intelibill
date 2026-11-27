using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0205Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OwnerChangeOrRevocationBeforeAcceptance_RefusesAndEmitsNoAcceptance(bool changeOwnerAuthority)
    {
        await using var host = await StartAsync();
        using var payer = host.Merchant.CreateClient();
        using var owner = host.Merchant.CreateClient();
        await SignInAsync(host.Merchant, payer);
        var shop = await SignInAsync(host.Merchant, owner, withShop: true);
        var account = await CreateAccountAsync(payer);
        var request = await RequestAsync(payer, account, shop);
        var path = ConsentPath(account, shop, request);
        await DecideAsync(owner, path, "grant");
        if (changeOwnerAuthority)
        {
            await ChangeOwnerAuthorityAsync(shop);
        }
        else
        {
            await DecideAsync(owner, path, "revoke");
        }

        using var denied = await owner.PostAsJsonAsync(path + "/accept", new { operationKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal("Billing.ConsentInvalid", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(0L, (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), $"""
            SELECT count(*) FROM public.shop_affiliation_consent_events WHERE request_id = '{request:D}' AND kind = 'Accepted'
            """))!);
    }

    [Fact]
    public async Task AcceptanceCommitAndLostAcknowledgement_PreserveOneHistoricalDecision()
    {
        var deliveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await StartAsync(inner => new LoseFirstAcknowledgement(deliveryStarted, resumeDelivery) { InnerHandler = inner });
        using var payer = host.Merchant.CreateClient();
        using var owner = host.Merchant.CreateClient();
        await SignInAsync(host.Merchant, payer);
        var shop = await SignInAsync(host.Merchant, owner, withShop: true);
        var account = await CreateAccountAsync(payer);
        var path = ConsentPath(account, shop, await RequestAsync(payer, account, shop));
        await DecideAsync(owner, path, "grant");
        Assert.Equal("Accepted", await DecideAsync(owner, path, "accept"));
        await deliveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await ChangeOwnerAuthorityAsync(shop);
        resumeDelivery.SetResult();
        await WaitForAffiliationAsync(payer, account, "Applied");

        var hosting = database.Bootstrap(host.Fixture.Owner.Database);
        Assert.Equal(0L, (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(hosting,
            "SELECT authority_version FROM public.shop_affiliations"))!);
        Assert.Equal(1L, await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM public.shop_affiliation_inbox"));
        await WaitForConsentAsync(owner, path, "Applied");
        Assert.Equal(2L, await P0202Fixture.CountAsync(database, host.Fixture, "SELECT count(*) FROM public.shop_affiliation_delivery_uses"));
    }

    private Task ChangeOwnerAuthorityAsync(Guid shop) => LicensingPostgreSqlFixture.ExecuteAsync(
        database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), $"""
        BEGIN;
        SELECT id FROM public.shops WHERE id = '{shop:D}' FOR UPDATE;
        UPDATE public.shop_memberships SET role = 'Manager', authority_version = authority_version + 1 WHERE shop_id = '{shop:D}';
        UPDATE public.shop_memberships SET role = 'Owner', authority_version = authority_version + 1 WHERE shop_id = '{shop:D}';
        COMMIT;
        """);

    internal static async Task WaitForConsentAsync(HttpClient owner, string path, string status)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var result = await owner.GetFromJsonAsync<JsonElement>(path, deadline.Token);
            if (result.GetProperty("status").GetString() == status)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
        }
    }

    private sealed class LoseFirstAcknowledgement(TaskCompletionSource started, TaskCompletionSource resume) : DelegatingHandler
    {
        private int _deliveryAttempts;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath != ShopAffiliationsV1.DeliveryPath ||
                Interlocked.Increment(ref _deliveryAttempts) != 1)
                return await base.SendAsync(request, cancellationToken);

            started.SetResult();
            await resume.Task.WaitAsync(cancellationToken);
            using var committed = await base.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
            throw new HttpRequestException("Fixture discarded the first committed acknowledgement.");
        }
    }

    internal static async Task<string?> DecideAsync(HttpClient owner, string path, string action, Guid? key = null)
    {
        using var response = await owner.PostAsJsonAsync(path + "/" + action, new { operationKey = key ?? Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString();
    }

    private static async Task WaitForAffiliationAsync(HttpClient payer, Guid account, string expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var path = new Uri($"/api/billing/accounts/{account:D}/affiliations", UriKind.Relative);
        while (!deadline.IsCancellationRequested)
        {
            var list = await payer.GetFromJsonAsync<JsonElement>(path, deadline.Token);
            if (list.GetProperty("requests").EnumerateArray().Any(row => row.GetProperty("status").GetString() == expected))
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(50), deadline.Token);
        }
        Assert.Fail($"No affiliation reached {expected}.");
    }
}
