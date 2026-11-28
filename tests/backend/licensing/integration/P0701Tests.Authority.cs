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
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RecipientAuthoritySeparatesCommercialAndShopNotices()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(database, jan31);
        var packageA = await paid.PackageAsync(2, ["North Shop", "South Shop"]);
        await PurchaseAsync(packageA, "Month");
        var packageB = await paid.PackageAsync(1, ["Unrelated B Shop"]);
        await PurchaseAsync(packageB, "Month");
        await P0608Tests.DrainAsync(paid);

        var roles = await SeedAuthorityRolesAsync(paid, packageA);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);

        await AssertPersistedRoleJobsAsync(paid.Hosts.LicensingOwner, packageA, packageB, roles);
        await AssertMerchantReadIsolationAsync(paid, packageA, packageB, roles);

        // Unavailable merchant authority is retryable with zero fabricated jobs.
        var failingAuthority = new ToggleNoticeAuthority();
        await using var retryFixture = await P0308Fixture.StartAsync(
            database,
            jan31,
            services: services => services.AddSingleton<IMerchantNoticeRecipientAuthority>(failingAuthority));
        var retryPkg = await retryFixture.PackageAsync(1, ["Retry Store"]);
        await PurchaseAsync(retryPkg, "Month");
        await P0608Tests.DrainAsync(retryFixture);

        var failedPass = await RunPlannerPassAsync(retryFixture.Hosts.Licensing.Services);
        Assert.Equal(1, failedPass.AccountsSkippedUnavailable);
        Assert.Equal(0, failedPass.Inserted);
        Assert.Equal(0L, await CountJobsForAccountAsync(retryFixture.Hosts.LicensingOwner, retryPkg.Account));
    }

    [Fact]
    public async Task ProviderPreviewEnforcesLiveAuthorityRedactionAndNoStore()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(database, jan31);
        var packageA = await paid.PackageAsync(1, ["Preview Alpha"]);
        await PurchaseAsync(packageA, "Month");
        var packageB = await paid.PackageAsync(1, ["Preview Beta"]);
        await PurchaseAsync(packageB, "Month");
        await P0608Tests.DrainAsync(paid);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);

        var beforeCount = await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, packageA.Account);
        Assert.True(beforeCount > 0);
        var catalogue = paid.Checkout.Trial.Catalogue;
        using var platformOwner = await catalogue.SignInOwnerAsync();
        using var billing = await SignInAdminWithAsync(catalogue, platformOwner, "p0701.billing@example.test", "billing");
        using var licensingMgr = await SignInAdminWithAsync(catalogue, platformOwner, "p0701.licmgr@example.test", "licensing-manager");
        using var support = await SignInAdminWithAsync(catalogue, platformOwner, "p0701.support@example.test", "support");
        using var security = await SignInAdminWithAsync(catalogue, platformOwner, "p0701.security@example.test", "security-administrator");
        var pathA = $"/api/admin/accounts/{packageA.Account:D}/notifications";

        await AssertCommercialAndRedactedPreviewAsync(pathA, billing, licensingMgr, support);
        await AssertDeniedCursorAndSessionRevocationAsync(paid, pathA, packageB.Account, platformOwner, billing, licensingMgr, security);
        Assert.Equal(beforeCount, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, packageA.Account));
    }

    private static async Task<AuthorityRoles> SeedAuthorityRolesAsync(P0308Fixture paid, P0308Fixture.Package packageA)
    {
        var noShopManager = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, packageA.Payer.Client, packageA.Account, noShopManager);

        var phoneManager = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, packageA.Payer.Client, packageA.Account, phoneManager);
        await SetPhoneOnlyUserAsync(paid.Checkout.Delivery.MerchantDb, phoneManager.UserId, "+919876543211");

        var staff = await SignInAsync(paid.Hosts, ShopRole.Staff, packageA.Shops[0].ShopId);
        var disabled = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, packageA.Payer.Client, packageA.Account, disabled);
        await LicensingPostgreSqlFixture.ExecuteAsync(paid.Checkout.Delivery.MerchantDb, $"UPDATE users SET is_login_enabled = false WHERE id = '{disabled.UserId:D}'");

        var unverified = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, packageA.Payer.Client, packageA.Account, unverified);
        await LicensingPostgreSqlFixture.ExecuteAsync(paid.Checkout.Delivery.MerchantDb, $"UPDATE users SET is_email_verified = false, is_phone_verified = false WHERE id = '{unverified.UserId:D}'");

        var removed = await SignInAsync(paid.Hosts);
        var removedId = await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, packageA.Payer.Client, packageA.Account, removed);
        await LicensingPostgreSqlFixture.ExecuteAsync(paid.Hosts.LicensingOwner, $"UPDATE billing_account_members SET status = {(int)BillingAccountMemberStatus.Removed}, revision = revision + 1 WHERE id = '{removedId:D}'");

        return new AuthorityRoles(noShopManager, phoneManager, staff, disabled, unverified, removed);
    }

    private static async Task AssertPersistedRoleJobsAsync(
        NpgsqlConnectionStringBuilder licensingDb,
        P0308Fixture.Package packageA,
        P0308Fixture.Package packageB,
        AuthorityRoles roles)
    {
        var ownerJobs = await ReadJobsForRecipientAsync(licensingDb, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{packageA.Payer.UserId:D}");
        Assert.Equal(5, ownerJobs.Count);
        Assert.All(ownerJobs, j => Assert.Equal([LicenseNoticesV1.ChannelInApp, LicenseNoticesV1.ChannelEmail], j.Channels));

        Assert.Equal(5, (await ReadJobsForRecipientAsync(licensingDb, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{roles.NoShopManager.UserId:D}")).Count);
        var phoneJobs = await ReadJobsForRecipientAsync(licensingDb, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{roles.PhoneManager.UserId:D}");
        Assert.Equal(5, phoneJobs.Count);
        Assert.All(phoneJobs, j => { Assert.Equal([LicenseNoticesV1.ChannelInApp], j.Channels); Assert.Null(j.ProtectedDestination); });

        foreach (var excluded in new[] { roles.Disabled.UserId, roles.Unverified.UserId, roles.Removed.UserId, packageB.Payer.UserId })
            Assert.Empty(await ReadJobsForRecipientAsync(licensingDb, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{excluded:D}"));

        var staffJobs = await ReadJobsForRecipientAsync(licensingDb, packageA.Account, LicenseNoticesV1.ScopeShop, $"user:{roles.Staff.UserId:D}");
        Assert.Equal(5, staffJobs.Count);
        Assert.All(staffJobs, j =>
        {
            Assert.Equal([LicenseNoticesV1.ChannelInApp], j.Channels);
            Assert.Null(j.ProtectedDestination);
            Assert.DoesNotContain("evidencedTermPricePaise", j.TemplateArgs);
        });

        var shop2OwnerJobs = await ReadJobsForRecipientAsync(licensingDb, packageA.Account, LicenseNoticesV1.ScopeShop, $"user:{packageA.Shops[1].UserId:D}");
        Assert.Equal(5, shop2OwnerJobs.Count);
        Assert.All(shop2OwnerJobs, j =>
        {
            Assert.Equal([LicenseNoticesV1.ChannelInApp, LicenseNoticesV1.ChannelEmail], j.Channels);
            Assert.DoesNotContain("North Shop", j.TemplateArgs);
        });
        Assert.Empty(await ReadJobsForRecipientAsync(licensingDb, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{packageA.Shops[1].UserId:D}"));
    }

    private static async Task AssertMerchantReadIsolationAsync(
        P0308Fixture paid,
        P0308Fixture.Package packageA,
        P0308Fixture.Package packageB,
        AuthorityRoles roles)
    {
        paid.At(Utc("2028-02-22T04:30:00Z"));
        using var staffRead = await roles.Staff.Client.GetAsync($"/api/licensing/shops/{packageA.Shops[0].ShopId:D}/notices");
        Assert.Equal(HttpStatusCode.OK, staffRead.StatusCode);
        Assert.True(staffRead.Headers.CacheControl?.NoStore);
        var staffBody = await staffRead.Content.ReadAsStringAsync();
        Assert.DoesNotContain("12000", staffBody);
        Assert.DoesNotContain(packageA.Account.ToString("D"), staffBody);
        Assert.DoesNotContain(packageB.Account.ToString("D"), staffBody);

        Assert.Equal(HttpStatusCode.Forbidden, (await roles.Staff.Client.GetAsync($"/api/billing/accounts/{packageA.Account:D}/notices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await packageA.Shops[1].Client.GetAsync($"/api/billing/accounts/{packageA.Account:D}/notices")).StatusCode);

        using var mgrRead = await roles.NoShopManager.Client.GetAsync($"/api/billing/accounts/{packageA.Account:D}/notices");
        Assert.Equal(HttpStatusCode.OK, mgrRead.StatusCode);
        Assert.Equal(12000L, Assert.Single((await mgrRead.Content.ReadFromJsonAsync<AccountLicenseNoticesV1>())!.Notices).EvidencedTermPricePaise);
        Assert.Equal(HttpStatusCode.Forbidden, (await roles.NoShopManager.Client.GetAsync($"/api/licensing/shops/{packageA.Shops[0].ShopId:D}/notices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await roles.NoShopManager.Client.GetAsync($"/api/billing/accounts/{packageB.Account:D}/notices")).StatusCode);
    }

    private static async Task AssertCommercialAndRedactedPreviewAsync(
        string pathA,
        P0110CookieBrowser billing,
        P0110CookieBrowser licensingMgr,
        P0110CookieBrowser support)
    {
        foreach (var admin in new[] { billing, licensingMgr })
        {
            using var resp = await admin.SendAsync(HttpMethod.Get, pathA);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.True(resp.Headers.CacheControl?.NoStore);
            var page = (await resp.Content.ReadFromJsonAsync<ProviderNotificationsPageV1>())!;
            Assert.True(page.CommercialIncluded);
            Assert.Contains(page.Items, item => item.Commercial is { EvidencedTermPricePaise: 12000L });
        }

        using var supportResp = await support.SendAsync(HttpMethod.Get, pathA);
        Assert.Equal(HttpStatusCode.OK, supportResp.StatusCode);
        Assert.True(supportResp.Headers.CacheControl?.NoStore);
        var body = await supportResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("12000", body);
        Assert.DoesNotContain("destinationHint", body);
        var redacted = JsonSerializer.Deserialize<ProviderNotificationsPageV1>(body, WebJson)!;
        Assert.False(redacted.CommercialIncluded);
        Assert.All(redacted.Items, item => Assert.Null(item.Commercial));
    }

    private static async Task AssertDeniedCursorAndSessionRevocationAsync(
        P0308Fixture paid,
        string pathA,
        Guid accountB,
        P0110CookieBrowser platformOwner,
        P0110CookieBrowser billing,
        P0110CookieBrowser licensingMgr,
        P0110CookieBrowser security)
    {
        foreach (var denied in new[] { platformOwner, security })
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.SendAsync(HttpMethod.Get, pathA)).StatusCode);

        var foreignJobId = (Guid)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            paid.Hosts.LicensingOwner,
            $"SELECT id FROM public.notification_jobs WHERE account_id = '{accountB:D}' ORDER BY id LIMIT 1"))!;
        using (var badCursor = await billing.SendAsync(HttpMethod.Get, $"{pathA}?after={foreignJobId:D}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
            Assert.Equal("Notifications.InvalidCursor", await P0206Fixture.CodeAsync(badCursor));
        }

        using (var signOut = await P0212Fixture.PostAsync(licensingMgr, "/api/admin/auth/sign-out", new { }))
            Assert.True(signOut.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await licensingMgr.SendAsync(HttpMethod.Get, pathA)).StatusCode);

        await P0206Fixture.GrantAsync(platformOwner, "p0701.billing@example.test", "security-administrator");
        using var revokedResp = await billing.SendAsync(HttpMethod.Get, pathA);
        Assert.True(revokedResp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    internal static async Task<Guid> InviteBillingManagerAsync(
        NpgsqlConnectionStringBuilder licensingOwner,
        HttpClient ownerClient,
        Guid accountId,
        Merchant recipient)
    {
        var path = $"/api/billing/accounts/{accountId:D}";
        using var issued = await ownerClient.PostAsJsonAsync(
            path + "/invitations",
            new { operationKey = Guid.NewGuid(), expectedOwnerRevision = 1, recipientKind = "email", recipient = recipient.Email });
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        var inbox = await recipient.Client.GetFromJsonAsync<JsonElement>("/api/billing/invitations");
        var invitation = Assert.Single(inbox.GetProperty("invitations").EnumerateArray())
            .GetProperty("invitationId").GetGuid();
        using var accepted = await recipient.Client.PostAsJsonAsync(
            $"/api/billing/invitations/{invitation:D}/accept",
            new { expectedInvitationRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return (Guid)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            licensingOwner,
            $"SELECT id FROM public.billing_account_members WHERE account_id = '{accountId:D}' AND merchant_user_id = '{recipient.UserId:D}' AND status = 1"))!;
    }

    internal static async Task<P0110CookieBrowser> SignInAdminWithAsync(
        P0206Fixture catalogue,
        P0110CookieBrowser owner,
        string email,
        params string[] capabilities)
    {
        var seed = await catalogue.EnrollOperatorAsync(owner, email);
        await P0206Fixture.GrantAsync(owner, email, capabilities);
        return await catalogue.SignInOperatorAsync(seed, email);
    }

    internal static async Task<List<PersistedJobRow>> ReadJobsForRecipientAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId,
        string scopeKind,
        string recipientKey)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, audience, threshold, locale, allowed_channels, protected_destination,
                template_arguments::text, source_revision, created_at, source_id
            FROM public.notification_jobs
            WHERE account_id = $1 AND scope_kind = $2 AND recipient_key = $3
            ORDER BY due_at ASC, threshold ASC
            """, conn);
        cmd.Parameters.AddWithValue(accountId);
        cmd.Parameters.AddWithValue(scopeKind);
        cmd.Parameters.AddWithValue(recipientKey);

        var rows = new List<PersistedJobRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new PersistedJobRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetFieldValue<string[]>(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetGuid(9)));
        }

        return rows;
    }

    internal sealed record PersistedJobRow(
        Guid Id,
        string Audience,
        string Threshold,
        string Locale,
        string[] Channels,
        string? ProtectedDestination,
        string TemplateArgs,
        string SourceRevision,
        DateTimeOffset CreatedAt,
        Guid SourceId);

    private sealed record AuthorityRoles(
        Merchant NoShopManager,
        Merchant PhoneManager,
        Merchant Staff,
        Merchant Disabled,
        Merchant Unverified,
        Merchant Removed);

    private sealed class ToggleNoticeAuthority : IMerchantNoticeRecipientAuthority
    {
        public bool Enabled => true;

        public Task<NoticeRecipientAuthorityResponseV1?> InspectAsync(
            NoticeRecipientAuthorityRequestV1 request,
            CancellationToken ct) =>
            Task.FromResult<NoticeRecipientAuthorityResponseV1?>(null);
    }
}
