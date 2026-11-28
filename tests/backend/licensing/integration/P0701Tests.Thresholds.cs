using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;
using Intelibill.Shared.Contracts.V1;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0701Tests
{
    [Fact]
    public async Task TrialThresholdsUnlockAtExactBoundariesAndHideSupersededDeadlinesAfterExtension()
    {
        var trialStart = Utc("2028-02-01T04:30:00Z");
        var trialEnd = Utc("2028-02-15T04:30:00Z");
        var trialClock = new P0201Clock { Now = trialStart };
        await using var delivery = await P0213Fixture.StartAsync(database, businessClock: trialClock);
        var trial = delivery.Trial;
        var shopOwner = await SignInAsync(trial.Hosts, ShopRole.Owner);
        var payer = await SignInAsync(trial.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        await P0213Fixture.LinkAsync(account, payer, shopOwner);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(shopOwner, trial.Accept())).Status);

        Assert.True((await RunPlannerPassAsync(trial.Hosts.Licensing.Services)).Inserted >= 4);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdTrialMinus3Days, Utc("2028-02-12T04:30:00Z")),
                (LicenseNoticesV1.ThresholdTrialMinus1Day, Utc("2028-02-14T04:30:00Z")),
            ],
            await ReadScheduledThresholdsAsync(trial.LicensingOwner, account, LicenseNoticesV1.ScopeAccount, $"user:{payer.UserId:D}"));

        await AssertCurrentThresholdBoundaryAsync(
            trialClock, payer.Client, shopOwner.Client, account, shopOwner.ShopId,
            Utc("2028-02-12T04:30:00Z"), expectedBeforeThreshold: null, expectedAtThreshold: LicenseNoticesV1.ThresholdTrialMinus3Days);
        await AssertCurrentThresholdBoundaryAsync(
            trialClock, payer.Client, shopOwner.Client, account, shopOwner.ShopId,
            Utc("2028-02-14T04:30:00Z"), expectedBeforeThreshold: LicenseNoticesV1.ThresholdTrialMinus3Days, expectedAtThreshold: LicenseNoticesV1.ThresholdTrialMinus1Day);

        // Trial never gains paid grace after expiry.
        trialClock.Now = trialEnd.AddHours(1);
        Assert.Equal(0, (await RunPlannerPassAsync(trial.Hosts.Licensing.Services)).Inserted);

        // Extend trial to Feb 25: history retains original rows, but current merchant views never present old Feb 12/14 deadlines.
        trialClock.Now = Utc("2028-02-10T04:30:00Z");
        await ExtendTrialAsync(trial.CatalogueManager!, account, shopOwner.ShopId, Utc("2028-02-25T04:30:00Z"));
        Assert.True((await RunPlannerPassAsync(trial.Hosts.Licensing.Services)).Inserted >= 4);
        Assert.Equal(4, (await ReadScheduledThresholdsAsync(trial.LicensingOwner, account, LicenseNoticesV1.ScopeAccount, $"user:{payer.UserId:D}")).Length);

        trialClock.Now = Utc("2028-02-13T04:30:00Z");
        var accAfterExt = await payer.Client.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{account:D}/notices");
        var shopAfterExt = await shopOwner.Client.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{shopOwner.ShopId:D}/notices");
        Assert.Empty(accAfterExt!.Notices);
        Assert.Empty(shopAfterExt!.Notices);
        Assert.Equal(Utc("2028-02-22T04:30:00Z"), accAfterExt.NextDueAt);
        Assert.Equal(Utc("2028-02-22T04:30:00Z"), shopAfterExt.NextDueAt);
    }

    [Fact]
    public async Task PaidMonthlyAndGraceThresholdsPresentOneCoherentCurrentNoticePerBoundary()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0609Tests.StartAsync(database, jan31);
        var package = await paid.PackageAsync(1, ["Main Store"]);
        var (_, paidTermId) = await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);

        Assert.True((await RunPlannerPassAsync(paid.Hosts.Licensing.Services)).Inserted >= 10);
        (string Threshold, DateTimeOffset DueAt)[] expectedPaid =
        [
            (LicenseNoticesV1.ThresholdPaidMinus7Days, Utc("2028-02-22T04:30:00Z")),
            (LicenseNoticesV1.ThresholdPaidMinus1Day, Utc("2028-02-28T04:30:00Z")),
            (LicenseNoticesV1.ThresholdGraceStart, Utc("2028-02-29T04:30:00Z")),
            (LicenseNoticesV1.ThresholdGraceMinus1Day, Utc("2028-03-06T04:30:00Z")),
            (LicenseNoticesV1.ThresholdGraceExpiry, Utc("2028-03-07T04:30:00Z")),
        ];
        Assert.Equal(
            expectedPaid,
            await ReadScheduledThresholdsAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeAccount, $"user:{package.Payer.UserId:D}", paidTermId));

        string? previous = null;
        foreach (var (threshold, dueAt) in expectedPaid)
        {
            await AssertCurrentThresholdBoundaryAsync(
                paid.Checkout.CheckoutClock, package.Payer.Client, package.Shops[0].Client,
                package.Account, package.Shops[0].ShopId, dueAt, previous, threshold);
            previous = threshold;
        }

        // At grace expiry all 5 historical rows still exist in PostgreSQL while merchant sees 1 coherent Expired notice.
        Assert.Equal(
            5,
            (await ReadScheduledThresholdsAsync(paid.Hosts.LicensingOwner, package.Account, LicenseNoticesV1.ScopeShop, $"user:{package.Shops[0].UserId:D}", paidTermId)).Length);
    }

    [Fact]
    public async Task LatePassShortenedRefundCoincidentGraceAndHandoffProduceOnlyUsefulThresholds()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0609Tests.StartAsync(database, jan31);

        // Late first pass inside grace skips past thresholds and schedules only useful future ones.
        var latePackage = await paid.PackageAsync(1, ["Late Store"]);
        var (_, lateTermId) = await PurchaseAsync(latePackage, "Month");
        await P0608Tests.DrainAsync(paid);
        paid.At(Utc("2028-03-02T04:30:00Z"));
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdGraceMinus1Day, Utc("2028-03-06T04:30:00Z")),
                (LicenseNoticesV1.ThresholdGraceExpiry, Utc("2028-03-07T04:30:00Z")),
            ],
            await ReadScheduledThresholdsAsync(paid.Hosts.LicensingOwner, latePackage.Account, LicenseNoticesV1.ScopeAccount, $"user:{latePackage.Payer.UserId:D}", lateTermId));

        // Actual shortened refund window via real P0609 EndAt (2 days after start) drops pre-start -7 day and grace.
        paid.At(jan31);
        var shortPackage = await paid.PackageAsync(1, ["Shortened Store"]);
        var (shortOrder, shortTermId) = await PurchaseAsync(shortPackage, "Month");
        await P0608Tests.DrainAsync(paid);
        await ApproveShortenedRefundAsync(paid, shortPackage, shortOrder.OrderId, jan31.AddDays(2));
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(
            [(LicenseNoticesV1.ThresholdPaidMinus1Day, jan31.AddDays(1))],
            await ReadScheduledThresholdsAsync(paid.Hosts.LicensingOwner, shortPackage.Account, LicenseNoticesV1.ScopeAccount, $"user:{shortPackage.Payer.UserId:D}", shortTermId));

        AssertCoincidentGraceAndHandoffPolicy(jan31);
    }

    [Fact]
    public async Task ConvertedTrialSchedulesOnlyPaidThresholdsAtAndAfterConversionInstant()
    {
        var trialStart = Utc("2028-02-01T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(database, trialStart);
        var shopOwner = await SignInAsync(paid.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(shopOwner.Client, "Converted Trial Account");
        await P0213Fixture.LinkAsync(account, shopOwner, shopOwner);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(shopOwner, paid.Checkout.Trial.Accept())).Status);

        // Convert the live trial via simulator checkout before either trial reminder is planned.
        var conversionInstant = Utc("2028-02-05T04:30:00Z");
        paid.At(conversionInstant);
        var plan = await P0301Tests.PublishPlanAsync(paid.Checkout.Trial);
        var convertedPkg = new P0308Fixture.Package(shopOwner, account, [shopOwner], plan);
        var (_, paidTermId) = await PurchaseAsync(convertedPkg, "Month");
        await P0608Tests.DrainAsync(paid);

        // Plan at exact conversion instant: both account & shop job sets contain paid thresholds and zero trial thresholds.
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var accountJobs = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, account, LicenseNoticesV1.ScopeAccount, $"user:{shopOwner.UserId:D}");
        var shopJobs = await ReadJobsForRecipientAsync(paid.Hosts.LicensingOwner, account, LicenseNoticesV1.ScopeShop, $"user:{shopOwner.UserId:D}");

        Assert.Equal(5, accountJobs.Count);
        Assert.All(accountJobs, job => Assert.Equal(paidTermId, job.SourceId));
        Assert.DoesNotContain(accountJobs, job => job.Threshold.StartsWith("Trial", StringComparison.Ordinal));
        Assert.Equal(5, shopJobs.Count);
        Assert.All(shopJobs, job => Assert.Equal(paidTermId, job.SourceId));
        Assert.DoesNotContain(shopJobs, job => job.Threshold.StartsWith("Trial", StringComparison.Ordinal));

        // Subsequent pass after conversion instant still inserts zero converted-trial thresholds.
        paid.At(conversionInstant.AddDays(1));
        Assert.Equal(0, (await RunPlannerPassAsync(paid.Hosts.Licensing.Services)).Inserted);
    }

    [Fact]
    public async Task RenewalComplimentaryGrantInvalidTermsAndClosureEnforceCurrentMerchantDisplay()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0609Tests.StartAsync(database, jan31);

        // Renewal hides predecessor deadline from current merchant reads while keeping successor thresholds.
        var renewPkg = await paid.PackageAsync(1, ["Renewed Store"]);
        var (_, predecessorTerm) = await PurchaseAsync(renewPkg, "Month");
        await P0608Tests.DrainAsync(paid);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var (_, renewStatus) = await RenewAsync(renewPkg, predecessorTerm, "Month");
        var successorTerm = renewStatus.GetProperty("termId").GetGuid();
        await P0608Tests.DrainAsync(paid);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(Utc("2028-02-22T04:30:00Z"));
        Assert.Empty((await renewPkg.Payer.Client.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{renewPkg.Account:D}/notices"))!.Notices);
        Assert.Equal(5, (await ReadScheduledThresholdsAsync(paid.Hosts.LicensingOwner, renewPkg.Account, LicenseNoticesV1.ScopeAccount, $"user:{renewPkg.Payer.UserId:D}", successorTerm)).Length);

        // Active complimentary grant covering an expired baseline suppresses expired operational shop banner.
        paid.At(jan31);
        var grantPkg = await paid.PackageAsync(1, ["Grant Store"]);
        await PurchaseAsync(grantPkg, "Month");
        await P0608Tests.DrainAsync(paid);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        await GrantComplimentaryAsync(paid.Checkout.Trial.CatalogueManager!, grantPkg.Account, grantPkg.Shops[0].ShopId, Utc("2028-03-07T00:00:00Z"), Utc("2028-03-27T00:00:00Z"));
        paid.At(Utc("2028-03-08T04:30:00Z"));
        Assert.Empty((await grantPkg.Shops[0].Client.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{grantPkg.Shops[0].ShopId:D}/notices"))!.Notices);

        // Invalid 2-day monthly, incomplete evidence, and closed shop produce 0 jobs.
        paid.At(jan31);
        var invalidAccount = await CreateAccountAsync(renewPkg.Payer.Client, "Invalid Terms");
        var badPaidTerm = await SeedTermAsync(paid.Hosts, invalidAccount, new TermSeed(jan31, jan31.AddDays(2), AnchorLocal: "2028-01-31 10:00:00"));
        var incompleteTerm = await SeedTermAsync(paid.Hosts, invalidAccount, new TermSeed(jan31, Utc("2028-02-29T04:30:00Z"), Basis: null));
        var closedPkg = await paid.PackageAsync(1, ["Closed Store"]);
        await PurchaseAsync(closedPkg, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0417Tests.CloseShopAsync(closedPkg.Shops[0], closedPkg.Account);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0L, await CountJobsForTermAsync(paid.Hosts.LicensingOwner, invalidAccount, badPaidTerm));
        Assert.Equal(0L, await CountJobsForTermAsync(paid.Hosts.LicensingOwner, invalidAccount, incompleteTerm));
        Assert.Equal(0L, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, closedPkg.Account));
    }

    [Fact]
    public async Task PartialAndFullSuspensionProjectSafeCommercialGuidanceWithoutDuplicateDelivery()
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(database, jan31);
        using var platformOwner = await paid.Checkout.Trial.Catalogue.SignInOwnerAsync();
        using var secAdmin = await SignInAdminWithAsync(paid.Checkout.Trial.Catalogue, platformOwner, "p0701.sec.part@example.test", "security-administrator");

        // Case 1: 1 of 2 shops suspended BEFORE initial planning -> partial qualifier, active shop still planned, no duplicates on release.
        var prePkg = await paid.PackageAsync(2, ["Suspended North", "Active South"]);
        var (_, preTerm) = await PurchaseAsync(prePkg, "Month");
        await P0608Tests.DrainAsync(paid);
        var preRestrictionId = await P0404Tests.SuspendAsync(
            secAdmin, $"/api/admin/accounts/{prePkg.Account:D}/restrictions", "Shop", prePkg.Shops[0].ShopId);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);

        paid.At(Utc("2028-02-22T04:30:00Z"));
        var preCommercial = await prePkg.Payer.Client.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{prePkg.Account:D}/notices");
        var preItem = Assert.Single(preCommercial!.Notices);
        Assert.True(preItem.PaymentDoesNotLiftSuspension);
        Assert.Equal(LicenseNoticesV1.GuidancePaidRenewalPartialSuspension, preItem.GuidanceCode);
        Assert.Equal(1, preItem.SuspendedShopCount);
        Assert.Single((await prePkg.Shops[1].Client.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{prePkg.Shops[1].ShopId:D}/notices"))!.Notices);
        Assert.Equal(5, (await ReadScheduledThresholdsAsync(paid.Hosts.LicensingOwner, prePkg.Account, LicenseNoticesV1.ScopeAccount, $"user:{prePkg.Payer.UserId:D}", preTerm)).Length);

        // Case 2: 1 of 2 shops suspended AFTER initial planning -> live projection flips safe, release restores normal, still 5 semantic jobs!
        paid.At(jan31);
        var postPkg = await paid.PackageAsync(2, ["Post North", "Post South"]);
        var (_, postTerm) = await PurchaseAsync(postPkg, "Month");
        await P0608Tests.DrainAsync(paid);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var postRestrictionId = await P0404Tests.SuspendAsync(
            secAdmin, $"/api/admin/accounts/{postPkg.Account:D}/restrictions", "Shop", postPkg.Shops[0].ShopId);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);

        paid.At(Utc("2028-02-22T04:30:00Z"));
        var duringSusp = Assert.Single((await postPkg.Payer.Client.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{postPkg.Account:D}/notices"))!.Notices);
        Assert.True(duringSusp.PaymentDoesNotLiftSuspension);
        Assert.Equal(LicenseNoticesV1.GuidancePaidRenewalPartialSuspension, duringSusp.GuidanceCode);

        await ReleaseRestrictionAsync(secAdmin, postPkg.Account, postRestrictionId);
        await ReleaseRestrictionAsync(secAdmin, prePkg.Account, preRestrictionId);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var afterRelease = Assert.Single((await postPkg.Payer.Client.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{postPkg.Account:D}/notices"))!.Notices);
        Assert.False(afterRelease.PaymentDoesNotLiftSuspension);
        Assert.Equal(LicenseNoticesV1.GuidancePaidRenewalDue, afterRelease.GuidanceCode);
        Assert.Equal(5, (await ReadScheduledThresholdsAsync(paid.Hosts.LicensingOwner, postPkg.Account, LicenseNoticesV1.ScopeAccount, $"user:{postPkg.Payer.UserId:D}", postTerm)).Length);
        Assert.Equal(5, (await ReadScheduledThresholdsAsync(paid.Hosts.LicensingOwner, prePkg.Account, LicenseNoticesV1.ScopeAccount, $"user:{prePkg.Payer.UserId:D}", preTerm)).Length);

        // Case 3: Full account suspension suppresses payment invitations and marks GraceExpiry SuspendedNoPaymentLift.
        paid.At(jan31);
        var fullPkg = await paid.PackageAsync(1, ["Full Suspended"]);
        var (_, fullTerm) = await PurchaseAsync(fullPkg, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0404Tests.SuspendAsync(secAdmin, $"/api/admin/accounts/{fullPkg.Account:D}/restrictions", "Account", null);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(
            [(LicenseNoticesV1.ThresholdGraceExpiry, Utc("2028-03-07T04:30:00Z"))],
            await ReadScheduledThresholdsAsync(paid.Hosts.LicensingOwner, fullPkg.Account, LicenseNoticesV1.ScopeAccount, $"user:{fullPkg.Payer.UserId:D}", fullTerm));
    }

    private static void AssertCoincidentGraceAndHandoffPolicy(DateTimeOffset jan31)
    {
        var anchor = new BillingAnchor(new DateTime(2028, 1, 31, 10, 0, 0, DateTimeKind.Unspecified), BillingCalendar.Zone, BillingPeriod.Month);
        var oneDayGraceFacts = new SubscriptionAccessFacts(
            jan31, Utc("2028-02-29T04:30:00Z"), TermEvidenceState.Complete, SubscriptionBasis.Paid,
            anchor, 0, RefundBoundary: new RefundAccessBoundary(jan31.AddDays(2), jan31.AddDays(3)));
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdPaidMinus1Day, jan31.AddDays(1)),
                (LicenseNoticesV1.ThresholdGraceStart, jan31.AddDays(2)),
                (LicenseNoticesV1.ThresholdGraceExpiry, jan31.AddDays(3)),
            ],
            NotificationSchedulePolicy.PlanThresholds(oneDayGraceFacts, jan31, false, false).Select(i => (i.Threshold, i.DueAt)).ToArray());

        var handoffFacts = new SubscriptionAccessFacts(
            jan31, Utc("2028-02-29T04:30:00Z"), TermEvidenceState.Complete, SubscriptionBasis.Paid,
            anchor, 0, HandoffEndsAt: Utc("2028-02-10T04:30:00Z"));
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdPaidMinus7Days, Utc("2028-02-03T04:30:00Z")),
                (LicenseNoticesV1.ThresholdPaidMinus1Day, Utc("2028-02-09T04:30:00Z")),
            ],
            NotificationSchedulePolicy.PlanThresholds(handoffFacts, jan31, false, false).Select(i => (i.Threshold, i.DueAt)).ToArray());
    }

    internal static async Task ExtendTrialAsync(
        P0110CookieBrowser catalogueManager,
        Guid accountId,
        Guid shopId,
        DateTimeOffset extendedEnd)
    {
        var extPath = $"/api/admin/accounts/{accountId:D}/shops/{shopId:D}/trial-extensions";
        using var previewResp = await P0212Fixture.PostAsync(
            catalogueManager,
            extPath + "/preview",
            new { endsAt = extendedEnd.ToString("O"), reason = "Extended pilot", reviewReference = "PILOT-924" });
        Assert.Equal(HttpStatusCode.OK, previewResp.StatusCode);
        var extPreview = await P0206Fixture.BodyAsync(previewResp);
        using var extSaved = await P0212Fixture.PostAsync(
            catalogueManager,
            extPath,
            new
            {
                operationKey = Guid.NewGuid(),
                endsAt = extPreview.GetProperty("endsAt").GetString(),
                reason = "Extended pilot",
                reviewReference = "PILOT-924",
                currentVersion = extPreview.GetProperty("currentVersion").GetInt64(),
                sourceHash = extPreview.GetProperty("sourceHash").GetString(),
                confirmed = true,
            });
        Assert.Equal(HttpStatusCode.Created, extSaved.StatusCode);
    }

    internal static async Task ReleaseRestrictionAsync(
        P0110CookieBrowser secAdmin,
        Guid accountId,
        Guid restrictionId)
    {
        var path = $"/api/admin/accounts/{accountId:D}/restrictions/{restrictionId:D}/release";
        var command = await P0404Tests.PreviewReleaseAsync(secAdmin, path);
        using var released = await P0212Fixture.PostAsync(secAdmin, path, command);
        Assert.Equal(HttpStatusCode.Created, released.StatusCode);
    }

    private static async Task ApproveShortenedRefundAsync(
        P0308Fixture paid,
        P0308Fixture.Package package,
        Guid orderId,
        DateTimeOffset shortenedEnd)
    {
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, orderId);
        var refundRequest = await P0609Tests.RequestRefundAsync(package, orderId, confirmation, 6000L);
        using var billingAdmin = await P0306Tests.SignInBillingAsync(paid.Checkout);
        var preview = await P0609Tests.PreviewAsync(billingAdmin, package.Account, refundRequest, 4500L, "EndAt", shortenedEnd);
        using var approved = await P0609Tests.ApproveAsync(
            billingAdmin, package.Account, refundRequest, P0609Tests.Command(preview, 4500L, "EndAt", shortenedEnd));
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        await P0610Tests.PumpRefundsAsync(paid);
        await P0608Tests.DrainAsync(paid);
    }

    private static async Task GrantComplimentaryAsync(
        P0110CookieBrowser compManager,
        Guid accountId,
        Guid shopId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        var compBase = P0212Fixture.Base(accountId);
        using var prevResp = await P0212Fixture.PostAsync(
            compManager,
            compBase + "/preview",
            new { shopIds = new[] { shopId }, startsAt = startsAt.ToString("O"), endsAt = endsAt.ToString("O"), memberAllowance = 3, reason = "Pilot courtesy" });
        Assert.Equal(HttpStatusCode.OK, prevResp.StatusCode);
        var prev = await P0206Fixture.BodyAsync(prevResp);
        using var saved = await P0212Fixture.PostAsync(
            compManager,
            compBase,
            new
            {
                operationKey = Guid.NewGuid(),
                shopIds = new[] { shopId },
                startsAt = startsAt.ToString("O"),
                endsAt = endsAt.ToString("O"),
                memberAllowance = 3,
                reason = "Pilot courtesy",
                expectedVersion = prev.GetProperty("expectedVersion").GetInt64(),
                sourceHash = prev.GetProperty("sourceHash").GetString(),
                confirmed = true,
            });
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
    }

    private static async Task AssertCurrentThresholdBoundaryAsync(
        P0201Clock clock,
        HttpClient payerClient,
        HttpClient shopClient,
        Guid accountId,
        Guid shopId,
        DateTimeOffset boundary,
        string? expectedBeforeThreshold,
        string expectedAtThreshold)
    {
        clock.Now = boundary.AddTicks(-10);
        var beforeAcc = await payerClient.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{accountId:D}/notices");
        var beforeShop = await shopClient.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{shopId:D}/notices");
        AssertSingleOrEmptyThreshold(beforeAcc!.Notices.Select(n => n.Threshold).ToArray(), expectedBeforeThreshold);
        AssertSingleOrEmptyThreshold(beforeShop!.Notices.Select(n => n.Threshold).ToArray(), expectedBeforeThreshold);

        foreach (var instant in new[] { boundary, boundary.AddTicks(10) })
        {
            clock.Now = instant;
            var acc = await payerClient.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{accountId:D}/notices");
            var shop = await shopClient.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{shopId:D}/notices");
            Assert.Equal([expectedAtThreshold], acc!.Notices.Select(n => n.Threshold).ToArray());
            Assert.Equal([expectedAtThreshold], shop!.Notices.Select(n => n.Threshold).ToArray());
        }
    }

    private static void AssertSingleOrEmptyThreshold(string[] actual, string? expected)
    {
        if (expected is null)
            Assert.Empty(actual);
        else
            Assert.Equal([expected], actual);
    }

    internal static async Task<(string Threshold, DateTimeOffset DueAt)[]> ReadScheduledThresholdsAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId,
        string scopeKind,
        string recipientKey,
        Guid? sourceId = null)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT threshold, due_at
            FROM public.notification_jobs
            WHERE account_id = $1
              AND scope_kind = $2
              AND recipient_key = $3
              AND ($4::uuid IS NULL OR source_id = $4::uuid)
            ORDER BY due_at ASC, threshold ASC
            """, conn);
        cmd.Parameters.AddWithValue(accountId);
        cmd.Parameters.AddWithValue(scopeKind);
        cmd.Parameters.AddWithValue(recipientKey);
        cmd.Parameters.AddWithValue((object?)sourceId ?? DBNull.Value);

        var list = new List<(string, DateTimeOffset)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
        return [.. list];
    }

    internal static async Task<long> CountJobsForTermAsync(NpgsqlConnectionStringBuilder settings, Guid accountId, Guid sourceId) =>
        (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            settings,
            $"SELECT count(*) FROM public.notification_jobs WHERE account_id = '{accountId:D}' AND source_id = '{sourceId:D}'"))!;

    internal static async Task<long> CountJobsForAccountAsync(NpgsqlConnectionStringBuilder settings, Guid accountId) =>
        (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            settings,
            $"SELECT count(*) FROM public.notification_jobs WHERE account_id = '{accountId:D}'"))!;
}
