using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0509Tests
{
    [P0509BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BrowserSaleRecoversAfterOrdinaryExpiry()
    {
        var evidence = CreateScenarioEvidence("ordinary-expiry");
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend");
        var pinned = EnsurePinnedBrowserBuild(frontend);
        using var staged = StagePinnedMerchantWeb(frontend, pinned);

        var start = P0202Fixture.WholeSecondNow();
        var termEnd = start.AddMinutes(3);
        await using var fixture = await P0213Fixture.StartAsync(
            database, browser: true, prepareProvider: true, businessClock: new P0201Clock { Now = start },
            merchantExtras: PermitSigningSettings(pinned));
        fixture.Hosts.Fixture.Clock.Now = start;

        var (shop, account, barcode) = await PrepareTemporaryShopAsync(fixture, start.AddDays(-1), termEnd);
        using var client = shop.Client;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = timeout.Token;

        await using var web = await StartMerchantWebAsync(staged.Root, client.BaseAddress!, ct);
        await using var browser = StartReleaseBrowser(frontend, evidence, web.Origin, shop.Email, barcode, "ordinary-expiry", pinned);
        var browserReport = CaptureBrowserAsync(browser.Process, evidence, ct);

        var queued = await WaitForPhaseWithLiveClockAsync(fixture, evidence, "queued", browser.Process, browserReport, ct);
        var clientSaleId = queued.GetProperty("clientSaleId").GetString()!;
        var invoiceNumber = queued.GetProperty("invoiceNumber").GetString()!;
        var deviceId = queued.GetProperty("deviceId").GetString()!;
        var permitId = queued.GetProperty("permitId").GetGuid();
        var grantId = queued.GetProperty("grantId").GetGuid();
        var soldAt = ParseInstant(queued.GetProperty("soldAt").GetString()!);
        Assert.Equal((termEnd, "LicenceEnd"), (ParseInstant(queued.GetProperty("validUntil").GetString()!), queued.GetProperty("limitingReason").GetString()));
        Assert.Equal(0L, await CountMerchantAsync(fixture, "sales", $"shop_id = '{shop.ShopId:D}'"));
        Assert.Equal(0L, await CountMerchantAsync(fixture, "offline_submissions", $"shop_id = '{shop.ShopId:D}'"));

        var syncAt = termEnd.AddMinutes(1);
        fixture.Hosts.Fixture.Clock.Now = syncAt;
        fixture.Hosts.StatusClock.Now = syncAt;
        using (var refusedReserve = await client.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId, blockSize = 25 }, ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refusedReserve.StatusCode);
            Assert.Equal("Licence.Expired", await P0206Fixture.CodeAsync(refusedReserve));
        }

        await WriteSignalAsync(evidence, "expired.ready", syncAt.ToString("O"), ct);
        var synced = await WaitForPhaseJsonAsync(evidence, "synced", browser.Process, browserReport, ct);
        await AssertSingleBrowserPassAsync(browser.Process, browserReport);

        var (submission, sale, replayStatus) = await AssertPostedOnceAndReplayAsync(
            fixture, shop, client, deviceId, clientSaleId, invoiceNumber, permitId, grantId, soldAt, syncAt, synced.GetProperty("wireSale").Clone(), ct);
        var deviceRef = await AssertOrdinaryExpiryProviderViewAsync(fixture, account, shop.ShopId, deviceId, termEnd, syncAt);

        await WriteCorrelatedEvidenceAsync(evidence, "postgres-ordinary-expiry.json", new
        {
            scenario = "ordinary-expiry", shopId = shop.ShopId, accountId = account, deviceId, deviceRef,
            clientSaleId, invoiceNumber, permitId, grantId, soldAt, termEnd, syncAt,
            saleId = sale.Id, submissionId = submission.Id,
            rawPayloadSha256 = queued.GetProperty("rawPayloadSha256").GetString(),
            wireSaleSha256 = synced.GetProperty("wireSaleSha256").GetString(),
            serverPayloadHash = submission.PayloadHash,
            signedPermitSha256 = queued.GetProperty("signedPermitSha256").GetString(),
            replayStatus,
        }, ct);
    }

    [P0509BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MissedSuspensionRetainsOneReview()
    {
        var evidence = CreateScenarioEvidence("missed-suspension");
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend");
        var pinned = EnsurePinnedBrowserBuild(frontend);
        using var staged = StagePinnedMerchantWeb(frontend, pinned);

        var start = P0202Fixture.WholeSecondNow();
        var businessClock = new P0201Clock { Now = start };
        await using var fixture = await P0213Fixture.StartAsync(
            database, browser: true, prepareProvider: true, businessClock: businessClock,
            merchantExtras: PermitSigningSettings(pinned));
        fixture.Hosts.Fixture.Clock.Now = start;

        var (shop, account, barcode) = await PreparePaidShopAsync(fixture, start);
        var (otherShop, _, _) = await PreparePaidShopAsync(fixture, start);
        var manager = await SignInAsync(fixture.Hosts, ShopRole.Manager, existingShop: shop.ShopId);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, existingShop: shop.ShopId);
        using var ownerClient = shop.Client;
        using var ownerClient2 = await CreateSecondSessionClientAsync(fixture, shop.Email);
        using var otherClient = otherShop.Client;
        using var managerClient = manager.Client;
        using var staffClient = staff.Client;
        using var securityAdmin = await P0403Tests.SignInSecurityAsync(fixture);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = timeout.Token;

        await using var web = await StartMerchantWebAsync(staged.Root, ownerClient.BaseAddress!, ct);
        await using var browser = StartReleaseBrowser(frontend, evidence, web.Origin, shop.Email, barcode, "missed-suspension", pinned);
        var browserReport = CaptureBrowserAsync(browser.Process, evidence, ct);

        var offline = await WaitForPhaseWithLiveClockAsync(fixture, evidence, "offline", browser.Process, browserReport, ct);
        var deviceId = offline.GetProperty("deviceId").GetString()!;
        var permitId = offline.GetProperty("permitId").GetGuid();
        var grantId = offline.GetProperty("grantId").GetGuid();
        var permitValidUntil = ParseInstant(offline.GetProperty("validUntil").GetString()!);
        var stockBefore = await ReadShopStockQuantityAsync(fixture, shop.ShopId);
        var seqBefore = await ReadInvoiceSequenceSumAsync(fixture, shop.ShopId);

        var effectiveAt = await ApplyAcknowledgedTradingSuspensionAsync(fixture, businessClock, securityAdmin, account, shop.ShopId);
        await WriteSignalAsync(evidence, "suspended.ready", effectiveAt.ToString("O"), ct);

        var queued = await WaitForPhaseJsonAsync(evidence, "queued", browser.Process, browserReport, ct);
        var clientSaleId = queued.GetProperty("clientSaleId").GetString()!;
        var invoiceNumber = queued.GetProperty("invoiceNumber").GetString()!;
        var soldAt = ParseInstant(queued.GetProperty("soldAt").GetString()!);
        Assert.True(soldAt >= effectiveAt && soldAt < permitValidUntil, $"Expected {effectiveAt:O} <= {soldAt:O} < {permitValidUntil:O}.");

        var syncAt = soldAt.AddSeconds(5);
        fixture.Hosts.Fixture.Clock.Now = syncAt;
        businessClock.Now = syncAt;
        await WriteSignalAsync(evidence, "reconnect.ready", syncAt.ToString("O"), ct);

        var synced = await WaitForPhaseJsonAsync(evidence, "synced", browser.Process, browserReport, ct);
        await AssertSingleBrowserPassAsync(browser.Process, browserReport);

        Assert.Equal(("NeedsReview", "OfflineLicenceHistory.RestrictionConflict"),
            (synced.GetProperty("queueStatusAfter").GetString(), synced.GetProperty("errorCode").GetString()));
        var caseId = synced.GetProperty("reviewCaseId").GetGuid();
        if (synced.TryGetProperty("responseBody", out var body) && body.ValueKind == JsonValueKind.Object)
        {
            var browserResult = Assert.Single(body.GetProperty("results").EnumerateArray());
            Assert.Equal(("NeedsReview", "OfflineLicenceHistory.RestrictionConflict", caseId), (
                browserResult.GetProperty("status").GetString(),
                browserResult.GetProperty("errors")[0].GetProperty("code").GetString(),
                browserResult.GetProperty("reviewCaseId").GetGuid()));
        }
        var wireSale = synced.GetProperty("wireSale").Clone();

        await RaceDuplicateSuspensionSyncAsync(ownerClient, ownerClient2, deviceId, wireSale, caseId, ct);
        var submission = await AssertHeldReviewDatabaseStateAsync(fixture, shop.ShopId, clientSaleId, invoiceNumber, soldAt, syncAt, permitId, grantId, stockBefore, seqBefore);
        await VerifyReviewAccessAndActionsAsync(ownerClient, managerClient, otherClient, staffClient, staff.UserId, deviceId, caseId, wireSale, effectiveAt, ct);
        var deviceRef = await AssertMissedSuspensionProviderViewAsync(fixture, account, shop.ShopId, invoiceNumber, permitValidUntil, syncAt);

        await WriteCorrelatedEvidenceAsync(evidence, "postgres-missed-suspension.json", new
        {
            scenario = "missed-suspension", shopId = shop.ShopId, accountId = account, deviceId, deviceRef,
            clientSaleId, invoiceNumber, permitId, grantId, effectiveAt, soldAt, permitValidUntil, syncAt,
            submissionId = submission.Id, reviewCaseId = caseId,
            rawPayloadSha256 = queued.GetProperty("rawPayloadSha256").GetString(),
            wireSaleSha256 = synced.GetProperty("wireSaleSha256").GetString(),
            serverPayloadHash = submission.PayloadHash,
            signedPermitSha256 = queued.GetProperty("signedPermitSha256").GetString(),
            salesCount = 0,
        }, ct);
    }

    private static async Task<(SubmissionEvidence Submission, SaleEvidence Sale, string ReplayStatus)> AssertPostedOnceAndReplayAsync(
        P0213Fixture fixture, Merchant shop, HttpClient client, string deviceId, string clientSaleId, string invoiceNumber,
        Guid permitId, Guid grantId, DateTimeOffset soldAt, DateTimeOffset syncAt, JsonElement wireSale, CancellationToken ct)
    {
        Assert.Equal(1L, await CountMerchantAsync(fixture, "sales", $"shop_id = '{shop.ShopId:D}' AND client_sale_id = '{clientSaleId}'"));
        var submission = await ReadSubmissionAsync(fixture, shop.ShopId, clientSaleId);
        var sale = await ReadSaleAsync(fixture, shop.ShopId, clientSaleId);
        Assert.NotNull(submission);
        Assert.NotNull(sale);
        Assert.Equal((sale!.Id, invoiceNumber, soldAt, syncAt, permitId, grantId, sale.RequestHash),
            (submission!.SaleId, submission.InvoiceNumber, submission.ClaimedSoldAt, submission.ReceivedAt,
             submission.LicencePermitId, submission.LicenceGrantId, submission.PayloadHash));
        Assert.Equal((invoiceNumber, soldAt, 50m, shop.UserId), (sale.InvoiceNumber, sale.SoldAt, sale.TotalAmount, sale.ActorUserId));
        Assert.False(string.IsNullOrWhiteSpace(sale.InvoiceDocumentJson));

        var replay = await SyncSingleAsync(client, deviceId, wireSale, ct);
        var replayStatus = replay.GetProperty("status").GetString()!;
        Assert.Equal(("duplicate", sale.Id), (replayStatus, replay.GetProperty("saleId").GetGuid()));
        Assert.Equal(1L, await CountMerchantAsync(fixture, "sales", $"shop_id = '{shop.ShopId:D}'"));
        Assert.Equal(1L, await CountMerchantAsync(fixture, "offline_submissions", $"shop_id = '{shop.ShopId:D}'"));
        var afterReplay = await ReadSubmissionAsync(fixture, shop.ShopId, clientSaleId);
        Assert.Equal((submission.Id, submission.ReceivedAt, submission.PayloadHash, submission.PayloadJson),
            (afterReplay!.Id, afterReplay.ReceivedAt, afterReplay.PayloadHash, afterReplay.PayloadJson));
        return (submission, sale, replayStatus);
    }

    private static async Task<string> AssertOrdinaryExpiryProviderViewAsync(
        P0213Fixture fixture, Guid account, Guid shopId, string deviceId, DateTimeOffset termEnd, DateTimeOffset syncAt)
    {
        await DrainBothAsync(fixture);
        using var support = await P0417Tests.SignInAsync(fixture, "support");
        fixture.Hosts.StatusClock.Now = termEnd.AddMicroseconds(-1);
        var beforeEnd = await ReadProviderDevicePermitsAsync(support, account, shopId);
        var deviceBefore = Assert.Single(beforeEnd.GetProperty("devices").EnumerateArray());
        Assert.Equal((OfflineDeviceObservationsV1.DeviceRef(shopId, deviceId), termEnd, syncAt, true, "Unknown"), (
            deviceBefore.GetProperty("deviceRef").GetString(),
            deviceBefore.GetProperty("knownPermitEnd").GetDateTimeOffset(),
            deviceBefore.GetProperty("lastSynchronizedAt").GetDateTimeOffset(),
            deviceBefore.GetProperty("outstanding").GetBoolean(),
            beforeEnd.GetProperty("disconnectedQueues").GetString()));
        fixture.Hosts.StatusClock.Now = termEnd;
        var deviceAt = Assert.Single((await ReadProviderDevicePermitsAsync(support, account, shopId)).GetProperty("devices").EnumerateArray());
        Assert.Equal((false, 0), (deviceAt.GetProperty("outstanding").GetBoolean(), deviceAt.GetProperty("outstandingPermits").GetInt32()));
        return deviceBefore.GetProperty("deviceRef").GetString()!;
    }

    private static async Task<DateTimeOffset> ApplyAcknowledgedTradingSuspensionAsync(
        P0213Fixture fixture, P0201Clock businessClock, P0110CookieBrowser securityAdmin, Guid account, Guid shopId)
    {
        var path = $"/api/admin/accounts/{account:D}/restrictions";
        businessClock.Now = P0202Fixture.WholeSecondNow();
        fixture.Hosts.Fixture.Clock.Now = businessClock.Now;
        var restrictionId = await P0404Tests.SuspendAsync(securityAdmin, path, "Shop", shopId, "Trading");
        await P0403Tests.AssertPropagationAsync(securityAdmin, path, restrictionId, "Pending");
        await DrainBothAsync(fixture);
        await P0403Tests.AssertPropagationAsync(securityAdmin, path, restrictionId, "Acknowledged");
        var cutoff = (DateTime)(await fixture.MerchantScalarAsync(
            $"SELECT suspension_effective_at FROM public.shop_license_states WHERE shop_id = '{shopId:D}'"))!;
        return new DateTimeOffset(DateTime.SpecifyKind(cutoff, DateTimeKind.Utc));
    }

    private static async Task RaceDuplicateSuspensionSyncAsync(
        HttpClient ownerClient, HttpClient ownerClient2, string deviceId, JsonElement wireSale, Guid caseId, CancellationToken ct)
    {
        var raced = await RunBarrierRaceAsync(
            () => SyncSingleAsync(ownerClient, deviceId, wireSale, ct),
            () => SyncSingleAsync(ownerClient2, deviceId, wireSale, ct));
        Assert.All(raced, row => Assert.Equal(("NeedsReview", "OfflineLicenceHistory.RestrictionConflict", caseId), (
            row.GetProperty("status").GetString(),
            row.GetProperty("errors")[0].GetProperty("code").GetString(),
            row.GetProperty("reviewCaseId").GetGuid())));
    }

    private static async Task<SubmissionEvidence> AssertHeldReviewDatabaseStateAsync(
        P0213Fixture fixture, Guid shopId, string clientSaleId, string invoiceNumber, DateTimeOffset soldAt,
        DateTimeOffset syncAt, Guid permitId, Guid grantId, decimal stockBefore, long seqBefore)
    {
        Assert.Equal(0L, await CountMerchantAsync(fixture, "sales", $"shop_id = '{shopId:D}'"));
        Assert.Equal(1L, await CountMerchantAsync(fixture, "offline_submissions", $"shop_id = '{shopId:D}'"));
        Assert.Equal(1L, await CountMerchantAsync(fixture, "reconciliation_issues", $"shop_id = '{shopId:D}'"));
        Assert.Equal(stockBefore, await ReadShopStockQuantityAsync(fixture, shopId));
        Assert.Equal(seqBefore, await ReadInvoiceSequenceSumAsync(fixture, shopId));
        var submission = await ReadSubmissionAsync(fixture, shopId, clientSaleId);
        Assert.NotNull(submission);
        Assert.Null(submission!.SaleId);
        Assert.Equal((invoiceNumber, soldAt, syncAt, permitId, grantId),
            (submission.InvoiceNumber, submission.ClaimedSoldAt, submission.ReceivedAt, submission.LicencePermitId, submission.LicenceGrantId));
        return submission;
    }

    private static async Task<string> AssertMissedSuspensionProviderViewAsync(
        P0213Fixture fixture, Guid account, Guid shopId, string invoiceNumber, DateTimeOffset permitValidUntil, DateTimeOffset syncAt)
    {
        await DrainBothAsync(fixture);
        using var support = await P0417Tests.SignInAsync(fixture, "support");
        fixture.Hosts.StatusClock.Now = syncAt.AddMinutes(1);
        var provider = await ReadProviderDevicePermitsAsync(support, account, shopId);
        var deviceRow = Assert.Single(provider.GetProperty("devices").EnumerateArray());
        Assert.Equal((permitValidUntil, syncAt, true, "Unknown"), (
            deviceRow.GetProperty("knownPermitEnd").GetDateTimeOffset(),
            deviceRow.GetProperty("lastSynchronizedAt").GetDateTimeOffset(),
            deviceRow.GetProperty("outstanding").GetBoolean(),
            provider.GetProperty("disconnectedQueues").GetString()));
        Assert.DoesNotContain(invoiceNumber, provider.GetRawText(), StringComparison.Ordinal);
        return deviceRow.GetProperty("deviceRef").GetString()!;
    }

    private static async Task VerifyReviewAccessAndActionsAsync(
        HttpClient owner, HttpClient manager, HttpClient otherTenant, HttpClient staff, Guid staffUserId,
        string deviceId, Guid caseId, JsonElement wireSale, DateTimeOffset effectiveAt, CancellationToken ct)
    {
        uint version = 0;
        foreach (var viewer in new[] { owner, manager })
        {
            using var get = await viewer.GetAsync($"/api/offline-reconciliation/{caseId:D}", ct);
            var detail = await P0418Tests.ReadAsync(get, HttpStatusCode.OK);
            Assert.Equal("LicenseHistoryReview", detail.GetProperty("issueType").GetString());
            Assert.Contains("OfflineLicenceHistory.RestrictionConflict",
                detail.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(["Escalate"], detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
            var history = detail.GetProperty("licenceHistory");
            Assert.Equal((false, false), (history.GetProperty("retryEligible").GetBoolean(), history.GetProperty("linkEligible").GetBoolean()));
            Assert.Equal(effectiveAt, history.GetProperty("observations")[0].GetProperty("sourceCompletionCutoff").GetDateTimeOffset());
            Assert.DoesNotContain("signedPermit", detail.GetRawText(), StringComparison.OrdinalIgnoreCase);
            version = detail.GetProperty("version").GetUInt32();
        }

        foreach (var forbidden in new[] { "RetryHistoricalLease", "LinkExistingSale", "PostAnyway", "Redate", "Renumber", "Delete" })
        {
            using var bad = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
            {
                operationId = Guid.NewGuid().ToString("N"), expectedVersion = version, action = forbidden,
                reason = "Disallowed review action", linkedOperationId = Guid.NewGuid(),
            }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        using (var crossGet = await otherTenant.GetAsync($"/api/offline-reconciliation/{caseId:D}", ct))
            Assert.Equal(HttpStatusCode.NotFound, crossGet.StatusCode);
        using (var crossPost = await otherTenant.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = version, action = "Escalate", reason = "Cross tenant",
        }, ct))
            Assert.Equal(HttpStatusCode.NotFound, crossPost.StatusCode);
        using (var staffForeign = await staff.GetAsync($"/api/offline-reconciliation/{caseId:D}", ct))
            Assert.Equal(HttpStatusCode.NotFound, staffForeign.StatusCode);

        var staffNode = JsonNode.Parse(wireSale.GetRawText())!.AsObject();
        staffNode["clientSaleId"] = Guid.NewGuid().ToString("N");
        staffNode["invoiceNumber"] = "A/2627/000002";
        staffNode["issuedByUserId"] = staffUserId.ToString("D");
        var staffSync = await SyncSingleAsync(staff, deviceId, JsonSerializer.SerializeToElement(staffNode), ct);
        Assert.Equal("NeedsReview", staffSync.GetProperty("status").GetString());
        var staffCaseId = staffSync.GetProperty("reviewCaseId").GetGuid();
        using var staffDetailResp = await staff.GetAsync($"/api/offline-reconciliation/{staffCaseId:D}", ct);
        var staffDetail = await P0418Tests.ReadAsync(staffDetailResp, HttpStatusCode.OK);
        var staffVersion = staffDetail.GetProperty("version").GetUInt32();
        Assert.Equal(["Escalate"], staffDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        using (var staffRetry = await staff.PostAsJsonAsync($"/api/offline-reconciliation/{staffCaseId:D}/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = staffVersion, action = "RetryHistoricalLease", reason = "Staff retry",
        }, ct))
            Assert.Equal(HttpStatusCode.Forbidden, staffRetry.StatusCode);
        using (var staffEscalate = await staff.PostAsJsonAsync($"/api/offline-reconciliation/{staffCaseId:D}/resolve", new
        {
            operationId = Guid.NewGuid().ToString("N"), expectedVersion = staffVersion, action = "Escalate", reason = "Staff escalates own held case",
        }, ct))
            Assert.Equal("Escalated", (await P0418Tests.ReadAsync(staffEscalate, HttpStatusCode.OK)).GetProperty("status").GetString());
    }
}
