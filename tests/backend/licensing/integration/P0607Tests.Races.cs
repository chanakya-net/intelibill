using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0607Tests
{
    [Theory]
    [InlineData("billing-owner")]
    [InlineData("shop-owner")]
    [InlineData("consent-revoked")]
    [InlineData("members")]
    [InlineData("withheld-permit")]
    public async Task ApplyingTransferSerializesWithCompetingMutation(string mutation)
    {
        using var rsa = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, rsa);
        var id = await ConsentedAsync(s, s.Permit.ValidUntil);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("reviewer@example.test");
        using var session = manager;
        var merchantSessionAt = s.F.Hosts.Fixture.Clock.Now;
        var operation = await ApproveAsync(s, id, manager);
        if (mutation == "withheld-permit")
        {
            var later = await P0606Tests.IssueRealPermitAsync(s.F, s.North, RequestedAt.AddMinutes(5), false, "Withheld till");
            Assert.True(later.ValidUntil <= s.Permit.ValidUntil);
            await P0606Tests.AssertPermitInsertExceedingCutoffFailsAsync(s.F.Checkout.Delivery.MerchantDb,
                s.North.ShopId, later.GrantId, s.Permit.ValidUntil);
            await s.F.Checkout.Delivery.DrainAsync(false); // Prepared receipt makes the schedule eligible for the due scan.
        }
        else
            await DrainAsync(s); // Prepared by merchant. Consent and capacity must still be live at consumption.
        if (mutation == "consent-revoked")
        {
            s.F.Hosts.Fixture.Clock.Now = merchantSessionAt;
            var payer = s.Source.Payer.Client;
            using var revoked = await DecideAsync(payer, ConsentPath(s.Source.Account, id, "source-billing-owner"), "revoke",
                await ReadAsync(payer, s.Source.Account, id));
            revoked.EnsureSuccessStatusCode();
        }
        if (mutation == "billing-owner")
            await SwapBillingOwnerAsync(s, s.Destination.Account, s.Destination.Payer.UserId, s.Source.Shops[1].UserId);
        if (mutation == "shop-owner")
            await LicensingPostgreSqlFixture.ExecuteAsync(s.F.Checkout.Delivery.MerchantDb, $"""
                UPDATE public.shop_memberships SET authority_version=authority_version+1
                WHERE shop_id='{s.North.ShopId}' AND user_id='{s.North.UserId}'
                """);
        if (mutation == "members")
            await P0606Tests.AddActiveMembersAsync(database, s.North.ShopId, 12);
        await AdvanceAsync(s, s.Permit.ValidUntil);
        await AssertStateAsync(manager, AdminPath(s, id), operation, "NeedsReview");
        Assert.Equal(s.Source.Account, await BindingAsync(s));
        Assert.Equal(0L, await s.F.CountAsync("coverage_transfer_bindings"));
    }

    [Fact]
    public async Task ConcurrentApprovalsReserveOneDecisionAndOneFundedSlot()
    {
        using var rsa = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, rsa);
        var id = await ConsentedAsync(s, s.Permit.ValidUntil);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("competing@example.test");
        using var session = manager;
        const string email = "second.reviewer@example.test";
        var catalogue = s.F.Checkout.Trial.Catalogue;
        using var owner = await catalogue.SignInOwnerAsync();
        var seed = await catalogue.EnrollOperatorAsync(owner, email);
        await P0206Fixture.GrantAsync(owner, email, "licensing-manager");
        using var second = await catalogue.SignInOperatorAsync(seed, email);
        var (review, csrf) = await AdminReviewAsync(s, id, manager);
        var path = AdminPath(s, id) + "/approve";
        var secondCsrf = await P0206Fixture.ProofAsync(second);
        var keys = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var results = await Task.WhenAll(
            manager.SendAsync(HttpMethod.Post, path, Approval(review, keys[0]), csrf),
            second.SendAsync(HttpMethod.Post, path, Approval(review, keys[1]), secondCsrf));
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], results.Select(r => r.StatusCode).Order());
        var winner = Array.FindIndex(results, r => r.StatusCode == HttpStatusCode.Created);
        var other = winner == 0 ? second : manager;
        using var privateReceipt = await other.SendAsync(HttpMethod.Get, AdminPath(s, id) + $"/operations/{keys[winner]}");
        Assert.Equal(HttpStatusCode.NotFound, privateReceipt.StatusCode);
        foreach (var result in results) result.Dispose();
        await DrainAsync(s);
        await AdvanceAsync(s, s.Permit.ValidUntil);
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_approvals"));
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_bindings"));
    }

    [Fact]
    public async Task RetentionSurvivesProposalExpiryWithNoCarry()
    {
        using var rsa = TestRsaKeys.Create(TestRsaKeyPurpose.General);
        await using var s = await StartAsync(database, rsa, sameOwner: true);
        var id = await ConsentedAsync(s, null);
        var (manager, _) = await s.F.Checkout.Trial.Catalogue.ManagerAsync("retention@example.test");
        using var session = manager;
        var operation = await ApproveAsync(s, id, manager);
        await DrainAsync(s);
        await AdvanceAsync(s, P0601Tests.February29);
        await AssertStateAsync(manager, AdminPath(s, id), operation, "Applied");
        Assert.Equal(s.Destination.Account, await BindingAsync(s));
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_bindings", "paid_through_at=cutover_at"));
        Assert.NotEqual("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, P0601Tests.February29));
    }

    internal static async Task AdvanceAsync(Scenario s, DateTimeOffset at)
    {
        s.F.At(at);
        s.F.Hosts.Fixture.Clock.Now = at;
        s.F.Checkout.Delivery.Transport.LeaseClock.Now = at.AddSeconds(1);
        await s.F.Checkout.Delivery.ScanAsync(true);
        await DrainAsync(s);
        await DrainAsync(s);
    }

    private static string AdminPath(Scenario s, Guid id) => $"/api/admin/accounts/{s.Source.Account}/coverage-transfers/{id}";

    private static async Task<(JsonElement Review, string? Csrf)> AdminReviewAsync(Scenario s, Guid id, P0110CookieBrowser manager)
    {
        s.F.Hosts.Fixture.Clock.Now = s.F.Checkout.Trial.Catalogue.Clock.GetUtcNow();
        var csrf = await P0206Fixture.ProofAsync(manager);
        using var response = await manager.SendAsync(HttpMethod.Post, AdminPath(s, id) + "/preview", new { }, csrf);
        response.EnsureSuccessStatusCode();
        return (await P0206Fixture.BodyAsync(response), csrf);
    }

    private static async Task<Guid> ApproveAsync(Scenario s, Guid id, P0110CookieBrowser manager)
    {
        var (review, csrf) = await AdminReviewAsync(s, id, manager);
        var key = Guid.NewGuid();
        using var decision = await manager.SendAsync(HttpMethod.Post, AdminPath(s, id) + "/approve", Approval(review, key), csrf);
        Assert.True(decision.StatusCode == HttpStatusCode.Created, await decision.Content.ReadAsStringAsync());
        return key;
    }
}
