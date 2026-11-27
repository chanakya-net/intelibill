using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Fact]
    public async Task OpposingTransfersCanReserveDistinctShopsInTheSameAccountPair()
    {
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa);
        var outgoing = await ConsentedAsync(s, s.Permit.ValidUntil);
        var incoming = await ProposeReverseAsync(s);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("pair@example.test");
        using var session = manager;
        var (forward, csrf) = await AdminReviewAsync(s, outgoing, manager);
        var reversePath = $"/api/admin/accounts/{s.Destination.Account}/coverage-transfers/{incoming}";
        using var response = await manager.SendAsync(HttpMethod.Post, reversePath + "/preview", new { }, csrf);
        response.EnsureSuccessStatusCode();
        var reverse = await P0206Fixture.BodyAsync(response);
        var forwardKey = Guid.NewGuid();
        var reverseKey = Guid.NewGuid();
        var results = await Task.WhenAll(
            manager.SendAsync(HttpMethod.Post, AdminPath(s, outgoing) + "/approve", Approval(forward, forwardKey), csrf),
            manager.SendAsync(HttpMethod.Post, reversePath + "/approve", Approval(reverse, reverseKey), csrf));
        foreach (var result in results)
        {
            using (result) Assert.True(result.StatusCode == HttpStatusCode.Created, await result.Content.ReadAsStringAsync());
        }
        await DrainAsync(s);
        await AssertStateAsync(manager, AdminPath(s, outgoing), forwardKey, "Scheduled");
        await AssertStateAsync(manager, reversePath, reverseKey, "Scheduled");
        Assert.Equal(2L, await s.F.CountAsync("coverage_transfer_approvals"));
        Assert.Equal(0L, await s.F.CountAsync("coverage_transfer_bindings"));
    }

    private static async Task<Guid> ProposeReverseAsync(Scenario s)
    {
        var payer = s.Destination.Payer;
        var path = RequestPath(s.Destination.Account, s.DestinationTerm);
        using var terms = await payer.Client.PostAsJsonAsync(path + "/preview", new {
            destinationAccountId = s.Source.Account, shopId = payer.ShopId, treatment = "ReviewedAllocation", cutoverAt = s.Permit.ValidUntil,
        });
        terms.EnsureSuccessStatusCode();
        using var proposal = await payer.Client.PostAsJsonAsync(path,
            RequestBody(await terms.Content.ReadFromJsonAsync<JsonElement>(), Guid.NewGuid()));
        proposal.EnsureSuccessStatusCode();
        var transfer = (await proposal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("transferId").GetGuid();
        await AcceptAsync(payer.Client, s.Destination.Account, transfer, "source-billing-owner");
        await AcceptAsync(s.Source.Payer.Client, s.Source.Account, transfer, "destination-billing-owner");
        var consent = ShopConsentPath(s.Destination.Account, payer.ShopId, transfer);
        using var accepted = await DecideAsync(payer.Client, consent, "accept", await payer.Client.GetFromJsonAsync<JsonElement>(consent));
        accepted.EnsureSuccessStatusCode();
        await s.F.Checkout.Delivery.DrainAsync(false);
        return transfer;
    }
}
