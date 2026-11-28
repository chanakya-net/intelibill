using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    [Fact]
    public async Task SignedAuthorityBoundaryEnforcesExactAffiliationAndMinimizesWirePersonalData()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(database, jan31);
        var packageA = await paid.PackageAsync(1, ["Bound Shop"]);
        var packageB = await paid.PackageAsync(1, ["Other Account Shop"]);
        var shopA = packageA.Shops[0];
        var staff = await SignInAsync(paid.Hosts, ShopRole.Staff, shopA.ShopId);
        var phoneManager = await SignInAsync(paid.Hosts);
        await SetPhoneOnlyUserAsync(paid.Checkout.Delivery.MerchantDb, phoneManager.UserId, "+919876543210");

        var currentAffiliation = (Guid)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            paid.Hosts.LicensingOwner,
            $"SELECT request_id FROM public.current_shop_affiliations WHERE shop_id = '{shopA.ShopId:D}'"))!;

        // 1. Valid fresh affiliation (confirmed consent even before projection arrives) returns roster with minimized PII.
        var (validRaw, validReply) = await CallSignedRecipientAuthorityAsync(
            paid, packageA.Account, shopA.ShopId, currentAffiliation, []);
        Assert.True(validReply.AffiliationMatches);
        Assert.DoesNotContain("verifiedPhone", validRaw);
        Assert.DoesNotContain("+919876543210", validRaw);
        Assert.DoesNotContain(staff.Email, validRaw, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(validReply.Actors);
        await AssertCommercialActorPrivacyAsync(paid, packageA, staff, phoneManager);
        var wireStaff = Assert.Single(validReply.ShopMembers, m => m.UserId == staff.UserId);
        Assert.Equal("Staff", wireStaff.ShopRole);
        Assert.Null(wireStaff.VerifiedEmail);
        var wireOwner = Assert.Single(validReply.ShopMembers, m => m.UserId == shopA.UserId);
        Assert.Equal("Owner", wireOwner.ShopRole);
        Assert.Equal(shopA.Email, wireOwner.VerifiedEmail);

        using var invalid = await SendRecipientRequestAsync(
            paid, AuthorityRequest(paid, packageA.Account, shopA.ShopId, currentAffiliation, [staff.UserId]));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.DoesNotContain(staff.Email, await invalid.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // 2. Missing projection WITHOUT separate consent evidence -> affiliationMatches=false, empty roster!
        var unlinkedShop = await SignInAsync(paid.Hosts, ShopRole.Owner);
        var (noConsentBody, noConsentReply) = await CallSignedRecipientAuthorityAsync(
            paid, packageA.Account, unlinkedShop.ShopId, Guid.NewGuid(), []);
        Assert.False(noConsentReply.AffiliationMatches);
        Assert.Empty(noConsentReply.Actors);
        Assert.DoesNotContain(unlinkedShop.Email, noConsentBody, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(noConsentReply.ShopMembers);

        // 3. Wrong account & obsolete affiliation for the same account/shop -> affiliationMatches=false, empty roster!
        await PurchaseAsync(packageA, "Month");
        await P0608Tests.DrainAsync(paid);
        var (_, wrongAccountReply) = await CallSignedRecipientAuthorityAsync(
            paid, packageB.Account, shopA.ShopId, currentAffiliation, []);
        Assert.False(wrongAccountReply.AffiliationMatches);
        Assert.Empty(wrongAccountReply.ShopMembers);

        var (_, obsoleteAffiliationReply) = await CallSignedRecipientAuthorityAsync(
            paid, packageA.Account, shopA.ShopId, Guid.NewGuid(), []);
        Assert.False(obsoleteAffiliationReply.AffiliationMatches);
        Assert.Empty(obsoleteAffiliationReply.ShopMembers);
    }

    private static async Task AssertCommercialActorPrivacyAsync(
        P0308Fixture paid, Package packageA, Merchant staff, Merchant phoneManager)
    {
        var (commercialRaw, commercialReply) = await CallSignedRecipientAuthorityAsync(
            paid, packageA.Account, null, null, [phoneManager.UserId, packageA.Payer.UserId]);
        Assert.Empty(commercialReply.ShopMembers);
        Assert.DoesNotContain(staff.Email, commercialRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+919876543210", commercialRaw);
        Assert.Equal(packageA.Payer.Email, Assert.Single(commercialReply.Actors, a => a.UserId == packageA.Payer.UserId).VerifiedEmail);
        var wirePhoneActor = Assert.Single(commercialReply.Actors, a => a.UserId == phoneManager.UserId);
        Assert.True(wirePhoneActor.IsPhoneVerified);
        Assert.Null(wirePhoneActor.VerifiedEmail);
    }

    [Fact]
    public void MaskHintStaysValidAtBothScalarBoundaries()
    {
        var address = "𐐨abc@" + new string('d', 12) + "𐐨" + new string('x', 62);
        var hint = NotificationPlanner.MaskEmail(address);
        Assert.NotEqual(address, hint);
        Assert.StartsWith("𐐨***@", hint);
        Assert.NotEmpty(new System.Text.UTF8Encoding(false, true).GetBytes(hint));
        Assert.NotEqual("***@example.test", NotificationPlanner.MaskEmail("***@example.test"));
    }

    [Theory]
    [InlineData("a", "***")]
    [InlineData("****", "***")]
    [InlineData("𐐨", "***")]
    public async Task OneCharacterEmailLocalPartIsMaskedInContactsJobsAndPreviewWhileDeliverable(string local, string expected)
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(database, jan31);
        var package = await paid.PackageAsync(1, ["Short Email Store"]);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);

        var ownerShortEmail = local + "@short-owner.test";
        var contactShortEmail = local + "@short-contact.test";
        await LicensingPostgreSqlFixture.ExecuteAsync(
            paid.Checkout.Delivery.MerchantDb,
            $"UPDATE users SET email = '{ownerShortEmail}' WHERE id = '{package.Payer.UserId:D}'");

        using var approveResp = await package.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{package.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(Guid.NewGuid(), MerchantUserId: null, contactShortEmail, "en-IN"));
        Assert.Equal(HttpStatusCode.Created, approveResp.StatusCode);
        var approveRaw = await approveResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(contactShortEmail, approveRaw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***@short-contact.test", approveRaw);

        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var dbHints = await ReadDestinationHintsAndProtectedAsync(paid.Hosts.LicensingOwner, package.Account);
        Assert.NotEmpty(dbHints);
        Assert.All(dbHints, row =>
        {
            Assert.NotEqual(ownerShortEmail, row.Hint);
            Assert.Equal(expected + "@short-owner.test", row.Hint);
            Assert.NotEmpty(new System.Text.UTF8Encoding(false, true).GetBytes(row.Hint!));
        });

        await using (var scope = paid.Hosts.Licensing.Services.CreateAsyncScope())
        {
            var dp = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
            var sample = dbHints[0];
            var protector = dp.CreateProtector(
                "InteliBill.Licensing.NotificationDestination/v1",
                package.Account.ToString("D"),
                sample.ScopeKind,
                sample.ScopeId.ToString("D"),
                sample.RecipientKey);
            Assert.Equal(ownerShortEmail, protector.Unprotect(sample.Protected!));
        }

        var catalogue = paid.Checkout.Trial.Catalogue;
        using var platformOwner = await catalogue.SignInOwnerAsync();
        using var billing = await SignInAdminWithAsync(catalogue, platformOwner, "p0701.shortmask@example.test", "billing");
        using var preview = await billing.SendAsync(HttpMethod.Get, $"/api/admin/accounts/{package.Account:D}/notifications");
        var previewRaw = await preview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.DoesNotContain(ownerShortEmail, previewRaw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***@short-owner.test", previewRaw);
    }

    private static async Task<(string RawBody, NoticeRecipientAuthorityResponseV1 Reply)> CallSignedRecipientAuthorityAsync(
        P0308Fixture paid,
        Guid accountId,
        Guid? shopId,
        Guid? affiliationRequestId,
        Guid[] actorIds)
    {
        var payload = AuthorityRequest(paid, accountId, shopId, affiliationRequestId, actorIds);
        using var resp = await SendRecipientRequestAsync(paid, payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var raw = await resp.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<NoticeRecipientAuthorityResponseV1>(raw, EntitlementSnapshotsV1.Json)!;
        Assert.True(parsed.IsValid(payload));
        return (raw, parsed);
    }

    private static NoticeRecipientAuthorityRequestV1 AuthorityRequest(
        P0308Fixture paid, Guid account, Guid? shop, Guid? affiliation, Guid[] actors) =>
        new(1, Guid.NewGuid(), paid.Checkout.Delivery.AuthClock.GetUtcNow().AddSeconds(15), account, affiliation, shop, actors, null, 50);

    private static async Task<HttpResponseMessage> SendRecipientRequestAsync(P0308Fixture paid, NoticeRecipientAuthorityRequestV1 payload)
    {
        var clock = paid.Checkout.Delivery.AuthClock;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, EntitlementSnapshotsV1.Json);
        using var req = await paid.Checkout.Delivery.Transport.SignedAsync(
            bytes,
            clock,
            merchant: false,
            path: LicenseNoticesV1.RecipientAuthorityPath);
        return await paid.Checkout.Delivery.MerchantApi.SendAsync(req);
    }

    private static Task SetPhoneOnlyUserAsync(NpgsqlConnectionStringBuilder merchantDb, Guid userId, string phone) =>
        LicensingPostgreSqlFixture.ExecuteAsync(
            merchantDb,
            $"UPDATE users SET is_email_verified = false, is_phone_verified = true, phone_number = '{phone}' WHERE id = '{userId:D}'");

    private static async Task<List<(string ScopeKind, Guid ScopeId, string RecipientKey, string? Hint, string? Protected)>>
        ReadDestinationHintsAndProtectedAsync(NpgsqlConnectionStringBuilder settings, Guid accountId)
    {
        var list = new List<(string, Guid, string, string?, string?)>();
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT scope_kind, scope_id, recipient_key, destination_hint, protected_destination FROM public.notification_jobs WHERE account_id = $1 AND destination_hint IS NOT NULL",
            conn);
        cmd.Parameters.AddWithValue(accountId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add((
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return list;
    }
}
