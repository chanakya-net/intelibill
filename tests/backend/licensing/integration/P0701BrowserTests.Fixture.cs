using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701BrowserTests
{
    private static async Task<SeededBrowserData> SeedAccountsAndScheduleAsync(P0308Fixture paid, CancellationToken ct)
    {
        var packageA = await paid.PackageAsync(2, ["North Shop", "South Shop"]);
        var (_, paidTermA) = await PurchaseAsync(packageA, "Month");
        await P0608Tests.DrainAsync(paid);

        var staff = await SignInAsync(paid.Hosts, ShopRole.Staff, packageA.Shops[0].ShopId);
        await using (var mdb = MerchantDb(paid.Hosts.Database))
        {
            mdb.ShopMemberships.Add(ShopMembership.Create(packageA.Shops[1].ShopId, staff.UserId, ShopRole.Staff, isDefault: false));
            await mdb.SaveChangesAsync(ct);
        }

        var billingManager = await SignInAsync(paid.Hosts);
        await P0701Tests.InviteBillingManagerAsync(paid.Hosts.LicensingOwner, packageA.Payer.Client, packageA.Account, billingManager);
        var contact = await SeedOptedInContactAsync(paid, packageA, ct);

        var packageB = await SeedShortTermAsync(paid);

        paid.At(Utc("2028-02-01T04:30:00Z"));
        var trialOwner = await SignInAsync(paid.Hosts, ShopRole.Owner);
        var trialAccount = await CreateAccountAsync(trialOwner.Client, "Trial Demo Account");
        await P0213Fixture.LinkAsync(trialAccount, trialOwner, trialOwner);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(trialOwner, paid.Checkout.Trial.Accept())).Status);

        var concurrent = await Task.WhenAll(
            P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services),
            P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services));
        var freshScopeReplay = await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0, freshScopeReplay.Inserted);

        var paidBefore = await CountDueAsync(paid.Hosts.LicensingOwner, packageA.Account, Utc("2028-02-22T04:29:59.999999Z"));
        var paidEqual = await CountDueAsync(paid.Hosts.LicensingOwner, packageA.Account, Utc("2028-02-22T04:30:00Z"));
        var paidAfter = await CountDueAsync(paid.Hosts.LicensingOwner, packageA.Account, Utc("2028-02-22T04:30:00.000001Z"));
        Assert.Equal(0L, paidBefore);
        Assert.True(paidEqual > 0L);
        Assert.Equal(paidEqual, paidAfter);

        var trialDue = await P0701Tests.ReadScheduledThresholdsAsync(
            paid.Hosts.LicensingOwner, trialAccount, LicenseNoticesV1.ScopeAccount, $"user:{trialOwner.UserId:D}");
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdTrialMinus3Days, Utc("2028-02-12T04:30:00Z")),
                (LicenseNoticesV1.ThresholdTrialMinus1Day, Utc("2028-02-14T04:30:00Z")),
            ],
            trialDue);

        paid.At(Utc("2028-02-22T04:30:00Z"));
        var expectedJobIds = await ReadAccountJobIdsAsync(paid.Hosts.LicensingOwner, packageA.Account, ct);
        Assert.True(expectedJobIds.Length >= 25);

        return new SeededBrowserData(
            packageA, packageB, paidTermA, staff, billingManager, contact, trialOwner,
            concurrent[0].Inserted + concurrent[1].Inserted, freshScopeReplay.Inserted,
            paidBefore, paidEqual, paidAfter, trialDue, expectedJobIds);
    }

    private static async Task<Package> SeedShortTermAsync(P0308Fixture paid)
    {
        var packageB = await paid.PackageAsync(1, ["Beta Shop"]);
        var (orderB, _) = await PurchaseAsync(packageB, "Month");
        await P0608Tests.DrainAsync(paid);
        var adminClock = paid.Checkout.Trial.Catalogue.Clock;
        P0609Tests.SetCredentialTime(paid, adminClock.GetUtcNow());
        adminClock.Advanced += now => P0609Tests.SetCredentialTime(paid, now);
        await P0701Tests.ApproveShortenedRefundAsync(paid, packageB, orderB.OrderId, Utc("2028-02-03T04:30:00Z"));
        return packageB;
    }

    private static async Task<Merchant> SeedOptedInContactAsync(P0308Fixture paid, P0308Fixture.Package packageA, CancellationToken ct)
    {
        var contact = await SignInAsync(paid.Hosts);
        using var approved = await packageA.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{packageA.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(Guid.NewGuid(), contact.UserId, contact.Email, "en-IN"),
            ct);
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        var created = (await approved.Content.ReadFromJsonAsync<BillingContactV1>(ct))!;
        using var optedIn = await contact.Client.PostAsJsonAsync(
            $"/api/billing/notice-contacts/{created.ContactId:D}/consent",
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), contact.Email, null, OptedIn: true, "en-IN"),
            ct);
        Assert.Equal(HttpStatusCode.OK, optedIn.StatusCode);
        return contact;
    }

    private static async Task<BrowserAdmins> EnrollAdminsAsync(P0308Fixture paid)
    {
        var catalogue = paid.Checkout.Trial.Catalogue;
        using var platformOwner = await catalogue.SignInOwnerAsync();
        var billingSeed = await P0611Tests.EnrollAsync(paid.Checkout, platformOwner, BillingAdminEmail, "billing");
        var supportSeed = await P0611Tests.EnrollAsync(paid.Checkout, platformOwner, SupportAdminEmail, "support");
        var securitySeed = await P0611Tests.EnrollAsync(paid.Checkout, platformOwner, SecurityAdminEmail, "security-administrator");
        catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
        return new BrowserAdmins(billingSeed, supportSeed, securitySeed);
    }

    private static object BuildFixturePayload(P0308Fixture paid, SeededBrowserData seeded, BrowserAdmins admins) =>
        new
        {
            accountA = seeded.PackageA.Account,
            accountB = seeded.PackageB.Account,
            northShopId = seeded.PackageA.Shops[0].ShopId,
            southShopId = seeded.PackageA.Shops[1].ShopId,
            trialOwnerEmail = seeded.TrialOwner.Email,
            trialShopId = seeded.TrialOwner.ShopId,
            shortTermEmail = seeded.PackageB.Shops[0].Email,
            shortTermShopId = seeded.PackageB.Shops[0].ShopId,
            staffEmail = seeded.Staff.Email,
            southOwnerEmail = seeded.PackageA.Shops[1].Email,
            billingManagerEmail = seeded.BillingManager.Email,
            contactEmail = seeded.Contact.Email,
            contactAccessToken = seeded.Contact.Client.DefaultRequestHeaders.Authorization!.Parameter,
            merchantPassword = P0201Tests.Password,
            billingAdminEmail = BillingAdminEmail,
            billingAdminSeed = admins.BillingSeed,
            supportAdminEmail = SupportAdminEmail,
            supportAdminSeed = admins.SupportSeed,
            securityAdminEmail = SecurityAdminEmail,
            securityAdminSeed = admins.SecuritySeed,
            adminPassword = P0114Tests.InviteePassword,
            adminTime = paid.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
            expectedJobIds = seeded.ExpectedJobIds,
        };
}
