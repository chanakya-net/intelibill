using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;
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
        var ownerA = packageA.Payer;
        var shop2Owner = packageA.Shops[1]; // Non-billing Shop Owner for shop 2
        var (_, termA) = await PurchaseAsync(packageA, "Month");
        await P0608Tests.DrainAsync(paid);

        // No-shop verified Billing Manager.
        var noShopManager = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, ownerA.Client, packageA.Account, noShopManager);

        // Phone-only verified Billing Manager (in-app only, no email).
        var phoneManager = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, ownerA.Client, packageA.Account, phoneManager);
        await LicensingPostgreSqlFixture.ExecuteAsync(
            paid.Checkout.Delivery.MerchantDb,
            $"""
            UPDATE users
            SET is_email_verified = false,
                is_phone_verified = true,
                phone_number = '+919876543210'
            WHERE id = '{phoneManager.UserId:D}'
            """);

        // Shop 1 Staff (operational in-app only).
        var staff = await SignInAsync(paid.Hosts, ShopRole.Staff, packageA.Shops[0].ShopId);

        // Disabled, unverified, and removed roles (must all be excluded).
        var disabledManager = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, ownerA.Client, packageA.Account, disabledManager);
        await LicensingPostgreSqlFixture.ExecuteAsync(
            paid.Checkout.Delivery.MerchantDb,
            $"UPDATE users SET is_login_enabled = false WHERE id = '{disabledManager.UserId:D}'");

        var unverifiedManager = await SignInAsync(paid.Hosts);
        await InviteBillingManagerAsync(paid.Hosts.LicensingOwner, ownerA.Client, packageA.Account, unverifiedManager);
        await LicensingPostgreSqlFixture.ExecuteAsync(
            paid.Checkout.Delivery.MerchantDb,
            $"UPDATE users SET is_email_verified = false, is_phone_verified = false WHERE id = '{unverifiedManager.UserId:D}'");

        var removedManager = await SignInAsync(paid.Hosts);
        var removedMembership = await InviteBillingManagerAsync(
            paid.Hosts.LicensingOwner,
            ownerA.Client,
            packageA.Account,
            removedManager);
        await LicensingPostgreSqlFixture.ExecuteAsync(
            paid.Hosts.LicensingOwner,
            $"UPDATE billing_account_members SET status = {(int)BillingAccountMemberStatus.Removed}, revision = revision + 1 WHERE id = '{removedMembership:D}'");

        // Unrelated Account B with its own owner & shop.
        var packageB = await paid.PackageAsync(1, ["Unrelated B Shop"]);
        await PurchaseAsync(packageB, "Month");
        await P0608Tests.DrainAsync(paid);

        // Existing-user billing contact (no billing role, no shop membership):
        // Step 1: approve-only -> 0 jobs.
        var contactUser = await SignInAsync(paid.Hosts);
        using var approveUserResp = await ownerA.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{packageA.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(Guid.NewGuid(), contactUser.UserId, contactUser.Email, "hi-IN"));
        Assert.Equal(HttpStatusCode.Created, approveUserResp.StatusCode);
        var userContact = (await approveUserResp.Content.ReadFromJsonAsync<BillingContactV1>())!;
        Assert.False(userContact.Eligible);

        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Empty(await ReadJobsForRecipientAsync(
            paid.Hosts.LicensingOwner,
            packageA.Account,
            LicenseNoticesV1.ScopeAccount,
            $"user:{contactUser.UserId:D}"));

        // Step 2: verified without opt-in -> still 0 jobs.
        using var verifyOnlyResp = await contactUser.Client.PostAsJsonAsync(
            $"/api/billing/notice-contacts/{userContact.ContactId:D}/consent",
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), contactUser.Email, ProofToken: null, OptedIn: false, Locale: "hi-IN"));
        Assert.Equal(HttpStatusCode.OK, verifyOnlyResp.StatusCode);
        var verifyOnly = (await verifyOnlyResp.Content.ReadFromJsonAsync<BillingContactV1>())!;
        Assert.NotNull(verifyOnly.VerifiedAt);
        Assert.Null(verifyOnly.OptedInAt);
        Assert.False(verifyOnly.Eligible);

        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Empty(await ReadJobsForRecipientAsync(
            paid.Hosts.LicensingOwner,
            packageA.Account,
            LicenseNoticesV1.ScopeAccount,
            $"user:{contactUser.UserId:D}"));

        // Step 3: explicit opt-in -> eligible CommercialContact (Email only, no account/shop read rights).
        using var optInResp = await contactUser.Client.PostAsJsonAsync(
            $"/api/billing/notice-contacts/{userContact.ContactId:D}/consent",
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), contactUser.Email, ProofToken: null, OptedIn: true, Locale: "hi-IN"));
        Assert.Equal(HttpStatusCode.OK, optInResp.StatusCode);
        var optedIn = (await optInResp.Content.ReadFromJsonAsync<BillingContactV1>())!;
        Assert.True(optedIn.Eligible);

        // Standalone address-only contact with single-use proof token (no merchant account or login).
        const string standaloneEmail = "accounts.payable@north-example.test";
        using var approveStandaloneResp = await ownerA.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{packageA.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(Guid.NewGuid(), MerchantUserId: null, standaloneEmail, Locale: "ta-IN"));
        Assert.Equal(HttpStatusCode.Created, approveStandaloneResp.StatusCode);
        var standaloneContact = (await approveStandaloneResp.Content.ReadFromJsonAsync<BillingContactV1>())!;
        Assert.False(string.IsNullOrWhiteSpace(standaloneContact.VerificationProofToken));

        await using (var scope = paid.Hosts.Licensing.Services.CreateAsyncScope())
        {
            var contactHandler = scope.ServiceProvider.GetRequiredService<BillingContactConsentHandler>();
            var badProof = await contactHandler.ConfirmStandaloneProofAsync(
                packageA.Account,
                standaloneContact.ContactId,
                new ConfirmBillingContactConsentRequestV1(
                    Guid.NewGuid(),
                    standaloneEmail,
                    ProofToken: "invalid-token",
                    OptedIn: true,
                    Locale: "ta-IN"),
                CancellationToken.None);
            Assert.True(badProof.IsError);

            var goodProof = await contactHandler.ConfirmStandaloneProofAsync(
                packageA.Account,
                standaloneContact.ContactId,
                new ConfirmBillingContactConsentRequestV1(
                    Guid.NewGuid(),
                    standaloneEmail,
                    ProofToken: standaloneContact.VerificationProofToken,
                    OptedIn: true,
                    Locale: "ta-IN"),
                CancellationToken.None);
            Assert.False(goodProof.IsError);
            Assert.True(goodProof.Value.Eligible);

            // Replay of the single-use proof token is refused.
            var replayProof = await contactHandler.ConfirmStandaloneProofAsync(
                packageA.Account,
                standaloneContact.ContactId,
                new ConfirmBillingContactConsentRequestV1(
                    Guid.NewGuid(),
                    standaloneEmail,
                    ProofToken: standaloneContact.VerificationProofToken,
                    OptedIn: true,
                    Locale: "ta-IN"),
                CancellationToken.None);
            Assert.True(replayProof.IsError);
        }

        // Member/contact overlap with alias casing: standalone contact with noShopManager's uppercase email
        // must deduplicate into noShopManager's existing billing-role job (zero extra email:<hash> rows).
        using var overlapResp = await ownerA.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{packageA.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(
                Guid.NewGuid(),
                MerchantUserId: null,
                Email: "  " + noShopManager.Email.ToUpperInvariant() + "  ",
                Locale: "en-IN"));
        Assert.Equal(HttpStatusCode.Created, overlapResp.StatusCode);
        var overlapContact = (await overlapResp.Content.ReadFromJsonAsync<BillingContactV1>())!;
        await using (var scope = paid.Hosts.Licensing.Services.CreateAsyncScope())
        {
            var contactHandler = scope.ServiceProvider.GetRequiredService<BillingContactConsentHandler>();
            var confirmed = await contactHandler.ConfirmStandaloneProofAsync(
                packageA.Account,
                overlapContact.ContactId,
                new ConfirmBillingContactConsentRequestV1(
                    Guid.NewGuid(),
                    noShopManager.Email.ToUpperInvariant(),
                    ProofToken: overlapContact.VerificationProofToken,
                    OptedIn: true,
                    Locale: "en-IN"),
                CancellationToken.None);
            Assert.False(confirmed.IsError);
        }

        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);

        // Verify exact persisted audience, channels, and payloads in PostgreSQL.
        var ownerJobs = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{ownerA.UserId:D}");
        Assert.Equal(5, ownerJobs.Count);
        Assert.All(ownerJobs, job =>
        {
            Assert.Equal(LicenseNoticesV1.AudienceCommercialBillingRole, job.Audience);
            Assert.Equal([LicenseNoticesV1.ChannelInApp, LicenseNoticesV1.ChannelEmail], job.Channels);
            Assert.NotNull(job.ProtectedDestination);
        });

        var noShopMgrJobs = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{noShopManager.UserId:D}");
        Assert.Equal(5, noShopMgrJobs.Count);
        Assert.Empty(await ReadJobsForRecipientAsync(
            paid.Hosts.LicensingOwner,
            packageA.Account,
            LicenseNoticesV1.ScopeAccount,
            NotificationPlanner.EmailRecipientKey(noShopManager.Email)));

        var phoneMgrJobs = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{phoneManager.UserId:D}");
        Assert.Equal(5, phoneMgrJobs.Count);
        Assert.All(phoneMgrJobs, job =>
        {
            Assert.Equal([LicenseNoticesV1.ChannelInApp], job.Channels);
            Assert.Null(job.ProtectedDestination);
        });

        var contactUserJobs = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{contactUser.UserId:D}");
        Assert.Equal(5, contactUserJobs.Count);
        Assert.All(contactUserJobs, job =>
        {
            Assert.Equal(LicenseNoticesV1.AudienceCommercialContact, job.Audience);
            Assert.Equal([LicenseNoticesV1.ChannelEmail], job.Channels);
            Assert.Equal("hi-IN", job.Locale);
        });

        var standaloneJobs = await ReadJobsForRecipientAsync(
            paid.Hosts.LicensingOwner,
            packageA.Account,
            LicenseNoticesV1.ScopeAccount,
            NotificationPlanner.EmailRecipientKey(standaloneEmail));
        Assert.Equal(5, standaloneJobs.Count);
        Assert.All(standaloneJobs, job =>
        {
            Assert.Equal(LicenseNoticesV1.AudienceCommercialContact, job.Audience);
            Assert.Equal([LicenseNoticesV1.ChannelEmail], job.Channels);
            Assert.Equal("ta-IN", job.Locale);
        });

        // Excluded users have 0 jobs on Account A.
        foreach (var excluded in new[] { disabledManager.UserId, unverifiedManager.UserId, removedManager.UserId, packageB.Payer.UserId })
        {
            Assert.Empty(await ReadJobsForRecipientAsync(
                paid.Hosts.LicensingOwner,
                packageA.Account,
                LicenseNoticesV1.ScopeAccount,
                $"user:{excluded:D}"));
        }

        // Staff gets InApp-only operational shop notices; non-billing Shop Owner gets InApp + Email operational notices.
        var staffJobs = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, packageA.Account, LicenseNoticesV1.ScopeShop, $"user:{staff.UserId:D}");
        Assert.Equal(5, staffJobs.Count);
        Assert.All(staffJobs, job =>
        {
            Assert.Equal(LicenseNoticesV1.AudienceShopStaffOperational, job.Audience);
            Assert.Equal([LicenseNoticesV1.ChannelInApp], job.Channels);
            Assert.Null(job.ProtectedDestination);
            Assert.DoesNotContain("evidencedTermPricePaise", job.TemplateArgs);
            Assert.DoesNotContain("coveredShopNames", job.TemplateArgs);
        });

        var shop2OwnerJobs = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, packageA.Account, LicenseNoticesV1.ScopeShop, $"user:{shop2Owner.UserId:D}");
        Assert.Equal(5, shop2OwnerJobs.Count);
        Assert.All(shop2OwnerJobs, job =>
        {
            Assert.Equal(LicenseNoticesV1.AudienceShopOwnerOperational, job.Audience);
            Assert.Equal([LicenseNoticesV1.ChannelInApp, LicenseNoticesV1.ChannelEmail], job.Channels);
            Assert.NotNull(job.ProtectedDestination);
            Assert.DoesNotContain("evidencedTermPricePaise", job.TemplateArgs);
            Assert.DoesNotContain("North Shop", job.TemplateArgs);
        });
        Assert.Empty(await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, packageA.Account, LicenseNoticesV1.ScopeAccount, $"user:{shop2Owner.UserId:D}"));

        // Live merchant read API isolation at first due instant (22 Feb 04:30 UTC).
        paid.At(Utc("2028-02-22T04:30:00Z"));
        using var staffRead = await staff.Client.GetAsync($"/api/licensing/shops/{packageA.Shops[0].ShopId:D}/notices");
        Assert.Equal(HttpStatusCode.OK, staffRead.StatusCode);
        Assert.True(staffRead.Headers.CacheControl?.NoStore);
        var staffBody = await staffRead.Content.ReadAsStringAsync();
        Assert.DoesNotContain("12000", staffBody);
        Assert.DoesNotContain("INR", staffBody);
        Assert.DoesNotContain(packageA.Account.ToString("D"), staffBody);
        Assert.DoesNotContain(packageB.Account.ToString("D"), staffBody);

        using var staffAccountDenied = await staff.Client.GetAsync($"/api/billing/accounts/{packageA.Account:D}/notices");
        Assert.Equal(HttpStatusCode.Forbidden, staffAccountDenied.StatusCode);
        using var shop2OwnerAccountDenied = await shop2Owner.Client.GetAsync($"/api/billing/accounts/{packageA.Account:D}/notices");
        Assert.Equal(HttpStatusCode.Forbidden, shop2OwnerAccountDenied.StatusCode);
        using var contactAccountDenied = await contactUser.Client.GetAsync($"/api/billing/accounts/{packageA.Account:D}/notices");
        Assert.Equal(HttpStatusCode.Forbidden, contactAccountDenied.StatusCode);
        using var contactShopDenied = await contactUser.Client.GetAsync($"/api/licensing/shops/{packageA.Shops[0].ShopId:D}/notices");
        Assert.Equal(HttpStatusCode.Forbidden, contactShopDenied.StatusCode);

        // No-shop billing manager reads Account A commercial notices, denied shop data and Account B.
        using var mgrAccountRead = await noShopManager.Client.GetAsync($"/api/billing/accounts/{packageA.Account:D}/notices");
        Assert.Equal(HttpStatusCode.OK, mgrAccountRead.StatusCode);
        var mgrNotices = (await mgrAccountRead.Content.ReadFromJsonAsync<AccountLicenseNoticesV1>())!;
        Assert.Single(mgrNotices.Notices);
        Assert.Equal(12000L, mgrNotices.Notices[0].EvidencedTermPricePaise);
        using var mgrShopDenied = await noShopManager.Client.GetAsync($"/api/licensing/shops/{packageA.Shops[0].ShopId:D}/notices");
        Assert.Equal(HttpStatusCode.Forbidden, mgrShopDenied.StatusCode);
        using var mgrAccountBDenied = await noShopManager.Client.GetAsync($"/api/billing/accounts/{packageB.Account:D}/notices");
        Assert.Equal(HttpStatusCode.Forbidden, mgrAccountBDenied.StatusCode);

        // Contact revocation by holder and by owner both record immutable Revoked events.
        using var holderRevoke = await contactUser.Client.PostAsJsonAsync(
            $"/api/billing/notice-contacts/{userContact.ContactId:D}/revoke",
            new RevokeBillingContactRequestV1(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, holderRevoke.StatusCode);
        Assert.NotNull((await holderRevoke.Content.ReadFromJsonAsync<BillingContactV1>())!.RevokedAt);

        using var ownerRevoke = await ownerA.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{packageA.Account:D}/notice-contacts/{standaloneContact.ContactId:D}/revoke",
            new RevokeBillingContactRequestV1(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, ownerRevoke.StatusCode);
        Assert.NotNull((await ownerRevoke.Content.ReadFromJsonAsync<BillingContactV1>())!.RevokedAt);

        // Unavailable / tampered merchant authority proof is retryable with zero fabricated jobs.
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
        Assert.Equal(termA, termA); // keep termA referenced cleanly
    }

    [Fact]
    public async Task RoleRemovalRaceWithEnqueueBlocksRevokedCommercialRecipient()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(
            database,
            jan31,
            settings: new Dictionary<string, string?>(P0302Fixture.SimulatorSettings)
            {
                ["Database:MaxPoolSize"] = "4",
                ["Notifications:PlanInBackground"] = "false",
            });
        var package = await paid.PackageAsync(1, ["Race Store"]);
        await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);

        var controlManager = await SignInAsync(paid.Hosts);
        var controlMembership = await InviteBillingManagerAsync(
            paid.Hosts.LicensingOwner,
            package.Payer.Client,
            package.Account,
            controlManager);

        await using var gate = new NpgsqlConnection(paid.Hosts.LicensingOwner.ConnectionString);
        await gate.OpenAsync();
        await using var monitor = new NpgsqlConnection(paid.Hosts.LicensingOwner.ConnectionString);
        await monitor.OpenAsync();

        // Control: hold FOR NO KEY UPDATE on membership, start planner, observe FOR SHARE wait, then ROLLBACK -> scheduled.
        await using (var barrier = await gate.BeginTransactionAsync())
        {
            await HoldMemberRowAsync(barrier, controlMembership);
            var planTask = RunPlannerPassAsync(paid.Hosts.Licensing.Services);
            await WaitForMemberShareLockAsync(monitor, planTask);
            await barrier.RollbackAsync();
            var controlResult = await planTask;
            Assert.True(controlResult.Inserted >= 5);
        }

        Assert.Equal(
            5,
            (await ReadJobsForRecipientAsync(
                paid.Hosts.LicensingOwner,
                package.Account,
                LicenseNoticesV1.ScopeAccount,
                $"user:{controlManager.UserId:D}")).Count);

        // Race: add a second manager, hold FOR NO KEY UPDATE, observe planner waiting on FOR SHARE, commit removal -> 0 rows.
        var revokedManager = await SignInAsync(paid.Hosts);
        var revokedMembership = await InviteBillingManagerAsync(
            paid.Hosts.LicensingOwner,
            package.Payer.Client,
            package.Account,
            revokedManager);

        await using (var barrier = await gate.BeginTransactionAsync())
        {
            await HoldMemberRowAsync(barrier, revokedMembership);
            var planTask = RunPlannerPassAsync(paid.Hosts.Licensing.Services);
            await WaitForMemberShareLockAsync(monitor, planTask);

            await using var remove = new NpgsqlCommand(
                "UPDATE billing_account_members SET status = $2, revision = revision + 1 WHERE id = $1",
                gate,
                barrier);
            remove.Parameters.AddWithValue(revokedMembership);
            remove.Parameters.AddWithValue((int)BillingAccountMemberStatus.Removed);
            Assert.Equal(1, await remove.ExecuteNonQueryAsync());
            await barrier.CommitAsync();

            var raceResult = await planTask;
            Assert.True(raceResult.SkippedAuthorityChanged >= 5);
        }

        Assert.Empty(await ReadJobsForRecipientAsync(
            paid.Hosts.LicensingOwner,
            package.Account,
            LicenseNoticesV1.ScopeAccount,
            $"user:{revokedManager.UserId:D}"));
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

        // Billing and Licensing Manager receive commercial preview with Cache-Control: no-store.
        foreach (var admin in new[] { billing, licensingMgr })
        {
            using var resp = await admin.SendAsync(HttpMethod.Get, pathA);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.True(resp.Headers.CacheControl?.NoStore);
            var page = (await resp.Content.ReadFromJsonAsync<ProviderNotificationsPageV1>())!;
            Assert.True(page.CommercialIncluded);
            Assert.Contains(page.Items, item => item.Commercial is { EvidencedTermPricePaise: 12000L });
        }

        // Support receives redacted preview (commercialIncluded = false, no price/destination hint).
        using (var resp = await support.SendAsync(HttpMethod.Get, pathA))
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.True(resp.Headers.CacheControl?.NoStore);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("12000", body);
            Assert.DoesNotContain("destinationHint", body);
            var page = JsonSerializer.Deserialize<ProviderNotificationsPageV1>(body, WebJson)!;
            Assert.False(page.CommercialIncluded);
            Assert.All(page.Items, item => Assert.Null(item.Commercial));
        }

        // PlatformOwner-only and SecurityAdministrator-only are denied (403).
        foreach (var denied in new[] { platformOwner, security })
        {
            using var resp = await denied.SendAsync(HttpMethod.Get, pathA);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        // Foreign account job ID as cursor returns 400 InvalidCursor.
        var foreignJobId = (Guid)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            paid.Hosts.LicensingOwner,
            $"SELECT id FROM public.notification_jobs WHERE account_id = '{packageB.Account:D}' ORDER BY id LIMIT 1"))!;
        using (var badCursor = await billing.SendAsync(HttpMethod.Get, $"{pathA}?after={foreignJobId:D}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
            Assert.Equal("Notifications.InvalidCursor", await P0206Fixture.CodeAsync(badCursor));
        }

        // Expired/revoked session returns 401.
        using (var signOut = await P0212Fixture.PostAsync(licensingMgr, "/api/admin/auth/sign-out", new { }))
            Assert.True(signOut.IsSuccessStatusCode, await signOut.Content.ReadAsStringAsync());
        using (var expiredResp = await licensingMgr.SendAsync(HttpMethod.Get, pathA))
            Assert.Equal(HttpStatusCode.Unauthorized, expiredResp.StatusCode);

        // Capability removal mid-session immediately revokes access on next read.
        await P0206Fixture.GrantAsync(platformOwner, "p0701.billing@example.test", "security-administrator");
        using (var revokedResp = await billing.SendAsync(HttpMethod.Get, pathA))
            Assert.True(revokedResp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        // Preview never writes rows.
        Assert.Equal(beforeCount, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, packageA.Account));
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
            .GetProperty("invitationId")
            .GetGuid();
        using var accepted = await recipient.Client.PostAsJsonAsync(
            $"/api/billing/invitations/{invitation:D}/accept",
            new { expectedInvitationRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return (Guid)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            licensingOwner,
            $"SELECT id FROM public.billing_account_members WHERE account_id = '{accountId:D}' AND merchant_user_id = '{recipient.UserId:D}' AND status = 1"))!;
    }

    private static async Task<P0110CookieBrowser> SignInAdminWithAsync(
        P0206Fixture catalogue,
        P0110CookieBrowser owner,
        string email,
        params string[] capabilities)
    {
        var seed = await catalogue.EnrollOperatorAsync(owner, email);
        await P0206Fixture.GrantAsync(owner, email, capabilities);
        return await catalogue.SignInOperatorAsync(seed, email);
    }

    private static async Task HoldMemberRowAsync(NpgsqlTransaction barrier, Guid membershipId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM billing_account_members WHERE id = $1 FOR NO KEY UPDATE",
            barrier.Connection,
            barrier);
        cmd.Parameters.AddWithValue(membershipId);
        Assert.Equal(membershipId, await cmd.ExecuteScalarAsync());
    }

    private static async Task WaitForMemberShareLockAsync(NpgsqlConnection monitor, Task<NotificationPassResult> pass)
    {
        await using var sql = new NpgsqlCommand("""
            SELECT count(*)
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND usename = 'licensing_runtime'
              AND wait_event_type = 'Lock'
              AND query LIKE '%billing_account_members%FOR SHARE%'
            """, monitor);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            if ((long)(await sql.ExecuteScalarAsync())! >= 1L)
                return;
            Assert.False(pass.IsCompleted, "Planner finished before waiting on billing_account_members FOR SHARE.");
            await Task.Delay(15);
        }

        Assert.Fail("Timed out waiting for planner FOR SHARE lock on billing_account_members.");
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
                template_arguments::text, source_revision, created_at
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
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<string[]>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8)));
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
        DateTimeOffset CreatedAt);

    private sealed class ToggleNoticeAuthority : IMerchantNoticeRecipientAuthority
    {
        public bool Enabled => true;

        public Task<NoticeRecipientAuthorityResponseV1?> InspectAsync(
            NoticeRecipientAuthorityRequestV1 request,
            CancellationToken ct) =>
            Task.FromResult<NoticeRecipientAuthorityResponseV1?>(null);
    }
}
