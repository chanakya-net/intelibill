using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task EffectiveTermProducesOnlyUsefulThresholds()
    {
        var trialStart = Utc("2028-02-01T04:30:00Z");
        var trialEnd = Utc("2028-02-15T04:30:00Z");
        var trialClock = new P0201Clock { Now = trialStart };
        await using (var trialDelivery = await P0213Fixture.StartAsync(database, businessClock: trialClock))
        {
            var trial = trialDelivery.Trial;
            var shopOwner = await SignInAsync(trial.Hosts, ShopRole.Owner);
            var payer = await SignInAsync(trial.Hosts);
            var account = await CreateAccountAsync(payer.Client);
            await trial.LinkAsync(account, payer.UserId, shopOwner);
            Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(shopOwner, trial.Accept())).Status);

            var firstPass = await RunPlannerPassAsync(trial.Hosts.Licensing.Services);
            Assert.True(firstPass.Inserted >= 4);

            var trialDue = await ReadScheduledThresholdsAsync(
                trial.LicensingOwner,
                account,
                LicenseNoticesV1.ScopeAccount,
                $"user:{payer.UserId:D}");
            Assert.Equal(
                [
                    (LicenseNoticesV1.ThresholdTrialMinus3Days, Utc("2028-02-12T04:30:00Z")),
                    (LicenseNoticesV1.ThresholdTrialMinus1Day, Utc("2028-02-14T04:30:00Z")),
                ],
                trialDue);

            // Exact -1 us, equality, and +1 us boundaries on both account and shop due reads.
            await AssertBoundaryUnlockAsync(
                trialClock,
                payer.Client,
                shopOwner.Client,
                account,
                shopOwner.ShopId,
                Utc("2028-02-12T04:30:00Z"),
                expectedBefore: 0,
                expectedAtAndAfter: 1);
            await AssertBoundaryUnlockAsync(
                trialClock,
                payer.Client,
                shopOwner.Client,
                account,
                shopOwner.ShopId,
                Utc("2028-02-14T04:30:00Z"),
                expectedBefore: 1,
                expectedAtAndAfter: 2);

            // Trial never receives paid grace after expiry.
            trialClock.Now = trialEnd.AddHours(1);
            var afterTrialEnd = await RunPlannerPassAsync(trial.Hosts.Licensing.Services);
            Assert.Equal(0, afterTrialEnd.Inserted);

            // Trial extension bumps trial_extensions.revision -> new source_revision -> schedules new thresholds.
            trialClock.Now = Utc("2028-02-10T04:30:00Z");
            var extendedEnd = Utc("2028-02-25T04:30:00Z");
            var extPath = $"/api/admin/accounts/{account:D}/shops/{shopOwner.ShopId:D}/trial-extensions";
            using var previewResp = await P0212Fixture.PostAsync(
                trial.CatalogueManager!,
                extPath + "/preview",
                new { endsAt = extendedEnd.ToString("O"), reason = "Extended pilot", reviewReference = "PILOT-924" });
            Assert.Equal(HttpStatusCode.OK, previewResp.StatusCode);
            var extPreview = await P0206Fixture.BodyAsync(previewResp);
            using var extSaved = await P0212Fixture.PostAsync(
                trial.CatalogueManager!,
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

            var extPass = await RunPlannerPassAsync(trial.Hosts.Licensing.Services);
            Assert.True(extPass.Inserted >= 4);
            var allTrialDue = await ReadScheduledThresholdsAsync(
                trial.LicensingOwner,
                account,
                LicenseNoticesV1.ScopeAccount,
                $"user:{payer.UserId:D}");
            Assert.Equal(
                [
                    (LicenseNoticesV1.ThresholdTrialMinus3Days, Utc("2028-02-12T04:30:00Z")),
                    (LicenseNoticesV1.ThresholdTrialMinus1Day, Utc("2028-02-14T04:30:00Z")),
                    (LicenseNoticesV1.ThresholdTrialMinus3Days, Utc("2028-02-22T04:30:00Z")),
                    (LicenseNoticesV1.ThresholdTrialMinus1Day, Utc("2028-02-24T04:30:00Z")),
                ],
                allTrialDue);
        }

        // Paid monthly term ending 2028-02-29T04:30:00Z (10:00 IST) -> -7/-1 and grace start/end-1/expiry.
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0609Tests.StartAsync(database, jan31);
        var package = await paid.PackageAsync(1, ["Main Store"]);
        var (_, paidTermId) = await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(paid);

        var paidPass = await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.True(paidPass.Inserted >= 10);

        var paidThresholds = await ReadScheduledThresholdsAsync(
            paid.Hosts.LicensingOwner,
            package.Account,
            LicenseNoticesV1.ScopeAccount,
            $"user:{package.Payer.UserId:D}",
            paidTermId);
        (string Threshold, DateTimeOffset DueAt)[] expectedPaid =
        [
            (LicenseNoticesV1.ThresholdPaidMinus7Days, Utc("2028-02-22T04:30:00Z")),
            (LicenseNoticesV1.ThresholdPaidMinus1Day, Utc("2028-02-28T04:30:00Z")),
            (LicenseNoticesV1.ThresholdGraceStart, Utc("2028-02-29T04:30:00Z")),
            (LicenseNoticesV1.ThresholdGraceMinus1Day, Utc("2028-03-06T04:30:00Z")),
            (LicenseNoticesV1.ThresholdGraceExpiry, Utc("2028-03-07T04:30:00Z")),
        ];
        Assert.Equal(expectedPaid, paidThresholds);

        for (var i = 0; i < expectedPaid.Length; i++)
        {
            await AssertBoundaryUnlockAsync(
                paid.Checkout.CheckoutClock,
                package.Payer.Client,
                package.Shops[0].Client,
                package.Account,
                package.Shops[0].ShopId,
                expectedPaid[i].DueAt,
                expectedBefore: i,
                expectedAtAndAfter: i + 1);
        }

        // Late first pass (clock starts inside grace at Mar 2) skips past thresholds and schedules only useful future ones.
        paid.At(jan31);
        var latePackage = await paid.PackageAsync(1, ["Late Store"]);
        var (_, lateTermId) = await PurchaseAsync(latePackage, "Month");
        await P0608Tests.DrainAsync(paid);
        paid.At(Utc("2028-03-02T04:30:00Z"));
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var lateThresholds = await ReadScheduledThresholdsAsync(
            paid.Hosts.LicensingOwner,
            latePackage.Account,
            LicenseNoticesV1.ScopeAccount,
            $"user:{latePackage.Payer.UserId:D}",
            lateTermId);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdGraceMinus1Day, Utc("2028-03-06T04:30:00Z")),
                (LicenseNoticesV1.ThresholdGraceExpiry, Utc("2028-03-07T04:30:00Z")),
            ],
            lateThresholds);

        // Actual shortened refund window via real P0609 EndAt (2 days after start) skips pre-start -7 day threshold & drops grace.
        paid.At(jan31);
        var shortPackage = await paid.PackageAsync(1, ["Shortened Store"]);
        var (shortOrder, shortTermId) = await PurchaseAsync(shortPackage, "Month");
        await P0608Tests.DrainAsync(paid);
        var confirmation = await P0608Tests.ConfirmationIdAsync(
            shortPackage.Payer.Client,
            shortPackage.Account,
            shortOrder.OrderId);
        var refundRequest = await P0609Tests.RequestRefundAsync(shortPackage, shortOrder.OrderId, confirmation, 6000L);
        using var billingAdmin = await P0306Tests.SignInBillingAsync(paid.Checkout);
        var shortenedEnd = jan31.AddDays(2);
        var refundPreview = await P0609Tests.PreviewAsync(
            billingAdmin,
            shortPackage.Account,
            refundRequest,
            4500L,
            "EndAt",
            shortenedEnd);
        using var approved = await P0609Tests.ApproveAsync(
            billingAdmin,
            shortPackage.Account,
            refundRequest,
            P0609Tests.Command(refundPreview, 4500L, "EndAt", shortenedEnd));
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        await P0610Tests.PumpRefundsAsync(paid);
        await P0608Tests.DrainAsync(paid);

        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var shortThresholds = await ReadScheduledThresholdsAsync(
            paid.Hosts.LicensingOwner,
            shortPackage.Account,
            LicenseNoticesV1.ScopeAccount,
            $"user:{shortPackage.Payer.UserId:D}",
            shortTermId);
        Assert.Equal(
            [(LicenseNoticesV1.ThresholdPaidMinus1Day, jan31.AddDays(1))],
            shortThresholds);

        // Coincident 1-day shortened grace window collapses GraceStart and GraceMinus1Day into one lifecycle transition.
        var anchor = new BillingAnchor(new DateTime(2028, 1, 31, 10, 0, 0, DateTimeKind.Unspecified), BillingCalendar.Zone, BillingPeriod.Month);
        var oneDayGraceFacts = new SubscriptionAccessFacts(
            jan31,
            Utc("2028-02-29T04:30:00Z"),
            TermEvidenceState.Complete,
            SubscriptionBasis.Paid,
            anchor,
            0,
            RefundBoundary: new RefundAccessBoundary(jan31.AddDays(2), jan31.AddDays(3)));
        var coincident = NotificationSchedulePolicy.PlanThresholds(oneDayGraceFacts, jan31, false, false);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdPaidMinus1Day, jan31.AddDays(1)),
                (LicenseNoticesV1.ThresholdGraceStart, jan31.AddDays(2)),
                (LicenseNoticesV1.ThresholdGraceExpiry, jan31.AddDays(3)),
            ],
            coincident.Select(item => (item.Threshold, item.DueAt)).ToArray());

        // Renewal before planning: predecessor is RenewedAtEnd -> 0 unpaid renewal/grace notices; successor gets its own.
        var renewPackage = await paid.PackageAsync(1, ["Renewed Store"]);
        var (_, predecessorTerm) = await PurchaseAsync(renewPackage, "Month");
        var (_, renewStatus) = await RenewAsync(renewPackage, predecessorTerm, "Month");
        var successorTerm = renewStatus.GetProperty("termId").GetGuid();
        await P0608Tests.DrainAsync(paid);

        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Empty(await ReadScheduledThresholdsAsync(
            paid.Hosts.LicensingOwner,
            renewPackage.Account,
            LicenseNoticesV1.ScopeAccount,
            $"user:{renewPackage.Payer.UserId:D}",
            predecessorTerm));
        Assert.Equal(
            5,
            (await ReadScheduledThresholdsAsync(
                paid.Hosts.LicensingOwner,
                renewPackage.Account,
                LicenseNoticesV1.ScopeAccount,
                $"user:{renewPackage.Payer.UserId:D}",
                successorTerm)).Length);

        // Fabricated 2-day monthly term -> Agrees rejects (NeedsReview) -> 0 notices; coverage-only row (Incomplete) -> 0 notices.
        var invalidAccount = await CreateAccountAsync(package.Payer.Client, "Invalid Terms");
        var badPaidTerm = await SeedTermAsync(
            paid.Hosts,
            invalidAccount,
            new TermSeed(jan31, jan31.AddDays(2), AnchorLocal: "2028-01-31 10:00:00"));
        var incompleteTerm = await SeedTermAsync(
            paid.Hosts,
            invalidAccount,
            new TermSeed(jan31, Utc("2028-02-29T04:30:00Z"), Basis: null));
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0L, await CountJobsForTermAsync(paid.Hosts.LicensingOwner, invalidAccount, badPaidTerm));
        Assert.Equal(0L, await CountJobsForTermAsync(paid.Hosts.LicensingOwner, invalidAccount, incompleteTerm));

        // Complimentary grant never produces paid renewal or grace notices.
        var compShop = await SignInAsync(paid.Hosts, ShopRole.Owner);
        var compAccount = await CreateAccountAsync(compShop.Client, "Comp Account");
        await P0213Fixture.LinkAsync(compAccount, compShop, compShop);
        var compManager = paid.Checkout.Trial.CatalogueManager!;
        var compBase = P0212Fixture.Base(compAccount);
        using var compPrevResp = await P0212Fixture.PostAsync(
            compManager,
            compBase + "/preview",
            new
            {
                shopIds = new[] { compShop.ShopId },
                startsAt = jan31.ToString("O"),
                endsAt = jan31.AddDays(20).ToString("O"),
                memberAllowance = 3,
                reason = "Pilot courtesy",
            });
        Assert.Equal(HttpStatusCode.OK, compPrevResp.StatusCode);
        var compPrev = await P0206Fixture.BodyAsync(compPrevResp);
        using var compSaved = await P0212Fixture.PostAsync(
            compManager,
            compBase,
            new
            {
                operationKey = Guid.NewGuid(),
                shopIds = new[] { compShop.ShopId },
                startsAt = jan31.ToString("O"),
                endsAt = jan31.AddDays(20).ToString("O"),
                memberAllowance = 3,
                reason = "Pilot courtesy",
                expectedVersion = compPrev.GetProperty("expectedVersion").GetInt64(),
                sourceHash = compPrev.GetProperty("sourceHash").GetString(),
                confirmed = true,
            });
        Assert.Equal(HttpStatusCode.Created, compSaved.StatusCode);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0L, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, compAccount));

        // Active suspension suppresses payment-invitation reminders and never implies payment lifts suspension.
        var suspPackage = await paid.PackageAsync(1, ["Suspended Store"]);
        var (_, suspTerm) = await PurchaseAsync(suspPackage, "Month");
        await P0608Tests.DrainAsync(paid);
        using var platformOwner = await paid.Checkout.Trial.Catalogue.SignInOwnerAsync();
        var secSeed = await paid.Checkout.Trial.Catalogue.EnrollOperatorAsync(platformOwner, "p0701.sec@example.test");
        await P0206Fixture.GrantAsync(platformOwner, "p0701.sec@example.test", "security-administrator");
        using var secAdmin = await paid.Checkout.Trial.Catalogue.SignInOperatorAsync(secSeed, "p0701.sec@example.test");
        await P0404Tests.SuspendAsync(secAdmin, $"/api/admin/accounts/{suspPackage.Account:D}/restrictions", "Account", null);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        var suspRows = await ReadScheduledThresholdsAsync(
            paid.Hosts.LicensingOwner,
            suspPackage.Account,
            LicenseNoticesV1.ScopeAccount,
            $"user:{suspPackage.Payer.UserId:D}",
            suspTerm);
        Assert.Equal(
            [(LicenseNoticesV1.ThresholdGraceExpiry, Utc("2028-03-07T04:30:00Z"))],
            suspRows);
        var suspArgs = (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            paid.Hosts.LicensingOwner,
            $"SELECT template_arguments::text FROM public.notification_jobs WHERE account_id = '{suspPackage.Account:D}' AND scope_kind = 'Account'"))!;
        Assert.Contains("\"paymentDoesNotLiftSuspension\":true", suspArgs.Replace(" ", string.Empty));
        Assert.Contains("\"guidanceCode\":\"SuspendedNoPaymentLift\"", suspArgs.Replace(" ", string.Empty));

        // Closure stops new planning for closed shops/accounts.
        var closedPackage = await paid.PackageAsync(1, ["Closed Store"]);
        await PurchaseAsync(closedPackage, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0417Tests.CloseShopAsync(closedPackage.Shops[0], closedPackage.Account);
        await RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0L, await CountJobsForAccountAsync(paid.Hosts.LicensingOwner, closedPackage.Account));

        // Handoff rule: outgoing shop gets paid warnings capped at handoff deadline and zero paid grace.
        var handoffFacts = new SubscriptionAccessFacts(
            jan31,
            Utc("2028-02-29T04:30:00Z"),
            TermEvidenceState.Complete,
            SubscriptionBasis.Paid,
            anchor,
            0,
            HandoffEndsAt: Utc("2028-02-10T04:30:00Z"));
        var handoffPlan = NotificationSchedulePolicy.PlanThresholds(handoffFacts, jan31, false, false);
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdPaidMinus7Days, Utc("2028-02-03T04:30:00Z")),
                (LicenseNoticesV1.ThresholdPaidMinus1Day, Utc("2028-02-09T04:30:00Z")),
            ],
            handoffPlan.Select(item => (item.Threshold, item.DueAt)).ToArray());
    }

    private static async Task AssertBoundaryUnlockAsync(
        P0201Clock clock,
        HttpClient payerClient,
        HttpClient shopClient,
        Guid accountId,
        Guid shopId,
        DateTimeOffset boundary,
        int expectedBefore,
        int expectedAtAndAfter)
    {
        clock.Now = boundary.AddTicks(-10);
        var beforeAcc = await payerClient.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{accountId:D}/notices");
        var beforeShop = await shopClient.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{shopId:D}/notices");
        Assert.Equal(expectedBefore, beforeAcc!.Notices.Length);
        Assert.Equal(expectedBefore, beforeShop!.Notices.Length);

        clock.Now = boundary;
        var atAcc = await payerClient.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{accountId:D}/notices");
        var atShop = await shopClient.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{shopId:D}/notices");
        Assert.Equal(expectedAtAndAfter, atAcc!.Notices.Length);
        Assert.Equal(expectedAtAndAfter, atShop!.Notices.Length);

        clock.Now = boundary.AddTicks(10);
        var afterAcc = await payerClient.GetFromJsonAsync<AccountLicenseNoticesV1>($"/api/billing/accounts/{accountId:D}/notices");
        var afterShop = await shopClient.GetFromJsonAsync<ShopLicenseNoticesV1>($"/api/licensing/shops/{shopId:D}/notices");
        Assert.Equal(expectedAtAndAfter, afterAcc!.Notices.Length);
        Assert.Equal(expectedAtAndAfter, afterShop!.Notices.Length);
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

    internal static async Task<long> CountJobsForTermAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId,
        Guid sourceId) =>
        (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            settings,
            $"SELECT count(*) FROM public.notification_jobs WHERE account_id = '{accountId:D}' AND source_id = '{sourceId:D}'"))!;

    internal static async Task<long> CountJobsForAccountAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId) =>
        (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(
            settings,
            $"SELECT count(*) FROM public.notification_jobs WHERE account_id = '{accountId:D}'"))!;
}
