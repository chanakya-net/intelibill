using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0606Tests
{
    [Fact]
    public async Task OnlyCurrentBillingAndBothShopOwnerConsentsAuthorizeTheReviewedTerms()
    {
        using var key = RSA.Create(2048);
        await using var f = await StartPermitFixtureAsync(database, key, January31);
        var (p, term) = await PrepareWithTradingNorthAsync(f);
        var permit = await IssueRealPermitAsync(f, p.Shops[2], EarlyStart, deliverObservation: true);

        // Create exception request by Billing Owner.
        var reqKey = Guid.NewGuid();
        var (id, _) = await CreateExceptionAsync(p, term, reqKey, EarlyStart, permit.ValidUntil, "CourtesyBridge", permit.ValidUntil);

        var outPath = OwnerConsentPath(p, p.Shops[2].ShopId, id);
        var inPath = OwnerConsentPath(p, p.Shops[3].ShopId, id);

        // Billing Owner cannot consent on behalf of North or South; North cannot consent for South.
        Assert.Equal(HttpStatusCode.Forbidden, (await P0604Tests.Decide(p.Payer.Client, outPath, "grant")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await P0604Tests.Decide(p.Payer.Client, inPath, "grant")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await P0604Tests.Decide(p.Shops[2].Client, inPath, "grant")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await P0604Tests.Decide(p.Shops[3].Client, outPath, "grant")).StatusCode);

        // Narrow owner disclosure: only this owner's leg and commercial dates, never sibling shops or provider notes.
        using var outRead = await p.Shops[2].Client.GetAsync(outPath);
        Assert.Equal(HttpStatusCode.OK, outRead.StatusCode);
        var outView = await outRead.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Outgoing", outView.GetProperty("leg").GetString());
        Assert.Equal("North", outView.GetProperty("shopName").GetString());
        Assert.Equal("South", outView.GetProperty("counterpartyShopName").GetString());
        Assert.False(outView.TryGetProperty("requestReason", out _));
        Assert.DoesNotContain("Central", outView.GetRawText(), StringComparison.Ordinal);

        // Outgoing accepts alone -> still AwaitingConsents, cannot approve yet.
        await GrantAndAcceptAsync(p.Shops[2].Client, outPath);
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions", $"exception_id='{id}' AND state='AwaitingConsents'"));

        var (manager, _) = await f.Checkout.Trial.Catalogue.ManagerAsync("consent.manager@example.test");
        using var managerScope = manager;
        var previewOne = await AdminPreviewAsync(manager, p.Account, id);
        Assert.False(previewOne.GetProperty("canApprove").GetBoolean());
        Assert.Contains("IncomingConsentMissing", previewOne.GetProperty("blockingReasons").EnumerateArray().Select(x => x.GetString()));

        // Incoming accepts -> ReadyForReview and canApprove == true.
        await GrantAndAcceptAsync(p.Shops[3].Client, inPath);
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions", $"exception_id='{id}' AND state='ReadyForReview'"));
        var previewReady = await AdminPreviewAsync(manager, p.Account, id);
        Assert.True(previewReady.GetProperty("canApprove").GetBoolean());

        // Revoking outgoing consent blocks the request and prevents approval.
        using var revoked = await P0604Tests.Decide(p.Shops[2].Client, outPath, "revoke");
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions", $"exception_id='{id}' AND state='Blocked' AND reason='ConsentRevoked'"));
        var previewRevoked = await AdminPreviewAsync(manager, p.Account, id);
        Assert.False(previewRevoked.GetProperty("canApprove").GetBoolean());

        // Attempt to approve with the stale ReadyForReview preview fails with 409.
        using var staleApprove = await AdminApproveAsync(manager, p.Account, id, Guid.NewGuid(), previewReady, "Approved despite revocation");
        Assert.Equal(HttpStatusCode.Conflict, staleApprove.StatusCode);

        // Excess seats on South refuses incoming accept (409 Coverage.MemberCapacityExceeded).
        var (idSeats, _) = await CreateExceptionAsync(p, term, Guid.NewGuid(), EarlyStart, permit.ValidUntil, "Wait", permit.ValidUntil);
        var inSeatsPath = OwnerConsentPath(p, p.Shops[3].ShopId, idSeats);
        Assert.Equal(HttpStatusCode.OK, (await P0604Tests.Decide(p.Shops[3].Client, inSeatsPath, "grant")).StatusCode);
        await AddActiveMembersAsync(database, p.Shops[3].ShopId, 5);
        using var overSeat = await P0604Tests.Decide(p.Shops[3].Client, inSeatsPath, "accept");
        Assert.Equal(HttpStatusCode.Conflict, overSeat.StatusCode);
        Assert.Equal("Coverage.MemberCapacityExceeded", (await overSeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // Reducing South's extra staff lets both owners complete consent -> ReadyForReview and canApprove == true.
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase), $"""
            UPDATE public.shop_memberships SET status = 'Suspended'
            WHERE shop_id = '{p.Shops[3].ShopId}' AND role = 'Staff' AND status = 'Active'
            """);
        Assert.Equal(HttpStatusCode.OK, (await P0604Tests.Decide(p.Shops[3].Client, inSeatsPath, "accept")).StatusCode);
        await GrantAndAcceptAsync(p.Shops[2].Client, OwnerConsentPath(p, p.Shops[2].ShopId, idSeats));
        await f.Checkout.Delivery.DrainAsync(false);
        var previewBeforeDemotion = await AdminPreviewAsync(manager, p.Account, idSeats);
        Assert.True(previewBeforeDemotion.GetProperty("canApprove").GetBoolean());

        // Demoting Billing Owner invalidates replay/recovery (403) and blocks the open proposal with BillingOwnerChanged.
        await LicensingPostgreSqlFixture.ExecuteAsync(f.Hosts.LicensingOwner, $"""
            INSERT INTO billing_account_members(id,account_id,merchant_user_id,role,status,revision,created_at)
            VALUES(gen_random_uuid(),'{p.Account}','{p.Shops[1].UserId}',2,1,1,now());
            UPDATE billing_account_members SET role=CASE WHEN merchant_user_id='{p.Payer.UserId}' THEN 2 ELSE 1 END
            WHERE account_id='{p.Account}' AND merchant_user_id IN ('{p.Payer.UserId}','{p.Shops[1].UserId}')
            """);
        using var replayAfterDemotion = await p.Payer.Client.PostAsJsonAsync(PathFor(p, term),
            BuildRequest(p, reqKey, EarlyStart, permit.ValidUntil, "CourtesyBridge", permit.ValidUntil));
        using var recoverAfterDemotion = await p.Payer.Client.GetAsync(PathFor(p, term) + "/operations/" + reqKey);
        Assert.Equal(HttpStatusCode.Forbidden, replayAfterDemotion.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, recoverAfterDemotion.StatusCode);

        var previewAfterDemotion = await AdminPreviewAsync(manager, p.Account, idSeats);
        Assert.False(previewAfterDemotion.GetProperty("canApprove").GetBoolean());
        Assert.Equal("Blocked", previewAfterDemotion.GetProperty("state").GetString());
        Assert.Contains("BillingOwnerChanged",
            previewAfterDemotion.GetProperty("blockingReasons").EnumerateArray().Select(x => x.GetString()));
        using var approveAfterDemotion = await AdminApproveAsync(manager, p.Account, idSeats, Guid.NewGuid(),
            previewBeforeDemotion, "Approve after billing owner demotion");
        Assert.Equal(HttpStatusCode.Conflict, approveAfterDemotion.StatusCode);
        Assert.Equal(0L, await f.CountAsync("coverage_replacement_exception_approvals"));
        await AssertZeroExceptionEffectsAsync(f, p.Account);
    }

    internal static async Task<(Guid ExceptionId, JsonElement Created)> CreateExceptionAsync(Package p, Guid term, Guid key,
        DateTimeOffset earlyStart, DateTimeOffset paidCutover, string disposition, DateTimeOffset? observedPermitDeadline,
        Guid? outgoing = null, Guid? incoming = null)
    {
        using var response = await p.Payer.Client.PostAsJsonAsync(PathFor(p, term),
            BuildRequest(p, key, earlyStart, paidCutover, disposition, observedPermitDeadline, outgoing, incoming));
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, raw);
        var body = JsonSerializer.Deserialize<JsonElement>(raw);
        return (body.GetProperty("exceptionId").GetGuid(), body);
    }

    internal static async Task GrantAndAcceptAsync(HttpClient client, string consentPath)
    {
        foreach (var decision in new[] { "grant", "accept" })
        {
            using var res = await P0604Tests.Decide(client, consentPath, decision);
            Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        }
    }

    internal static async Task<Guid> CreateConsentedExceptionAsync(P0308Fixture f, Package p, Guid term,
        DateTimeOffset earlyStart, DateTimeOffset paidCutover, string disposition, DateTimeOffset? observedPermitDeadline,
        P0209Fixture.Merchant? outgoingShop = null, P0209Fixture.Merchant? incomingShop = null)
    {
        var outShop = outgoingShop ?? p.Shops[2];
        var inShop = incomingShop ?? p.Shops[3];
        var (id, _) = await CreateExceptionAsync(p, term, Guid.NewGuid(), earlyStart, paidCutover, disposition,
            observedPermitDeadline, outShop.ShopId, inShop.ShopId);
        await GrantAndAcceptAsync(outShop.Client, OwnerConsentPath(p, outShop.ShopId, id));
        await GrantAndAcceptAsync(inShop.Client, OwnerConsentPath(p, inShop.ShopId, id));
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacement_exceptions", $"exception_id='{id}' AND state='ReadyForReview'"));
        return id;
    }

    internal static async Task<JsonElement> AdminPreviewAsync(P0110CookieBrowser manager, Guid account, Guid exceptionId)
    {
        using var res = await manager.SendAsync(HttpMethod.Post, AdminPreviewPath(account, exceptionId),
            new { }, await P0206Fixture.ProofAsync(manager));
        var raw = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.OK, raw);
        return JsonSerializer.Deserialize<JsonElement>(raw);
    }

    internal static async Task<HttpResponseMessage> AdminApproveAsync(P0110CookieBrowser manager, Guid account,
        Guid exceptionId, Guid opKey, JsonElement preview, string reason,
        long? proposalRevision = null, string? sourceHash = null, string? termsDigest = null,
        long? grantVersion = null, string? disposition = null, bool confirmed = true) =>
        await manager.SendAsync(HttpMethod.Post, AdminApprovePath(account, exceptionId), new
        {
            operationKey = opKey,
            expectedProposalRevision = proposalRevision ?? preview.GetProperty("proposalRevision").GetInt64(),
            expectedSourceHash = sourceHash ?? preview.GetProperty("sourceHash").GetString(),
            expectedTermsDigest = termsDigest ?? preview.GetProperty("termsDigest").GetString(),
            expectedGrantVersion = grantVersion ?? preview.GetProperty("grantScopeVersion").GetInt64(),
            disposition = disposition ?? preview.GetProperty("overlapDisposition").GetString(),
            reason,
            acknowledgedPermitsAndUnknownQueues = true,
            confirmed,
        }, await P0206Fixture.ProofAsync(manager));

    private static async Task AddActiveMembersAsync(LicensingPostgreSqlFixture db, Guid shopId, int count)
    {
        var merchant = db.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase);
        for (var i = 0; i < count; i++)
        {
            var userId = Guid.NewGuid();
            var email = $"extra-{i}-{userId:N}@example.test";
            await LicensingPostgreSqlFixture.ExecuteAsync(merchant, $"""
                INSERT INTO public.users (id, email, first_name, last_name, password_hash, language, is_email_verified, is_login_enabled, created_at)
                VALUES ('{userId}', '{email}', 'Staff', 'Member {i}', 'hash', 'en-IN', true, true, now());
                INSERT INTO public.shop_memberships (id, shop_id, user_id, role, status, authority_version, is_default, created_at)
                VALUES (gen_random_uuid(), '{shopId}', '{userId}', 'Staff', 'Active', 1, false, now());
                """);
        }
    }
}
