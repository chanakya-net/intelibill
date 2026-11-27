using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
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

        var queuedJson = await WaitForPhaseWithLiveClockAsync(fixture, evidence, "queued", browser.Process, browserReport, ct);
        var permit = HeldPermit.From(queuedJson);
        var queued = QueuedSale.From(queuedJson);
        Assert.Equal((termEnd, "LicenceEnd"), (permit.ValidUntil, queuedJson.GetProperty("limitingReason").GetString()));
        Assert.Equal(0L, await CountMerchantAsync(fixture, "sales", $"shop_id = '{shop.ShopId:D}'"));
        Assert.Equal(0L, await CountMerchantAsync(fixture, "offline_submissions", $"shop_id = '{shop.ShopId:D}'"));

        var syncAt = termEnd.AddMinutes(1);
        await AssertNewLeaseRefusedAfterExpiryAsync(fixture, client, permit.DeviceId, syncAt, ct);
        await WriteSignalAsync(evidence, "expired.ready", syncAt.ToString("O"), ct);
        var synced = await WaitForPhaseJsonAsync(evidence, "synced", browser.Process, browserReport, ct);
        await AssertSingleBrowserPassAsync(browser.Process, browserReport);

        var wireSale = AssertWireCarriesQueuedPermit(synced, queued);
        var (submission, sale, replayStatus, replayAt) = await AssertPostedOnceAndReplayAsync(fixture, shop, client, permit, queued, syncAt, wireSale, ct);
        AssertBrowserSawCreatedSale(synced, sale.Id);
        var deviceRef = await AssertOrdinaryExpiryProviderViewAsync(fixture, account, shop.ShopId, permit.DeviceId, termEnd, replayAt);

        await WriteCorrelatedEvidenceAsync(evidence, "postgres-ordinary-expiry.json", new
        {
            scenario = "ordinary-expiry", shopId = shop.ShopId, accountId = account, deviceId = permit.DeviceId, deviceRef,
            clientSaleId = queued.ClientSaleId, invoiceNumber = queued.InvoiceNumber, permitId = permit.PermitId, grantId = permit.GrantId,
            permitNotBefore = permit.NotBefore, soldAt = queued.SoldAt, termEnd, syncAt, replayAt,
            saleId = sale.Id, submissionId = submission.Id, receivedAt = submission.ReceivedAt,
            rawPayloadSha256 = queued.RawPayloadSha256,
            wireSaleSha256 = synced.GetProperty("wireSaleSha256").GetString(),
            serverPayloadHash = submission.PayloadHash,
            signedPermitSha256 = queued.SignedPermitSha256,
            wireSignedPermitSha256 = SignedPermitSha256(wireSale),
            replayStatus,
        }, ct);
    }

    [P0509BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MissedSuspensionRetainsOneReview()
    {
        var evidence = CreateScenarioEvidence("missed-suspension");
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        var pinned = EnsurePinnedBrowserBuild(frontend);
        using var staged = StagePinnedMerchantWeb(frontend, pinned);

        var start = P0202Fixture.WholeSecondNow();
        var businessClock = new P0201Clock { Now = start };
        await using var fixture = await P0213Fixture.StartAsync(
            database, browser: true, prepareProvider: true, businessClock: businessClock,
            merchantExtras: PermitSigningSettings(pinned));
        fixture.Hosts.Fixture.Clock.Now = start;

        var (shop, account, barcode) = await PreparePaidShopAsync(fixture, start);
        using var actors = await ReviewActors.CreateAsync(fixture, shop, start);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = timeout.Token;

        await using var web = await StartMerchantWebAsync(staged.Root, actors.Owner.BaseAddress!, ct);
        await using var browser = StartReleaseBrowser(frontend, evidence, web.Origin, shop.Email, barcode, "missed-suspension", pinned);
        var browserReport = CaptureBrowserAsync(browser.Process, evidence, ct);

        var permit = HeldPermit.From(await WaitForPhaseWithLiveClockAsync(fixture, evidence, "offline", browser.Process, browserReport, ct));
        var baseline = (Stock: await ReadShopStockQuantityAsync(fixture, shop.ShopId), Sequence: await ReadInvoiceSequenceSumAsync(fixture, shop.ShopId));

        var effectiveAt = await ApplyAcknowledgedTradingSuspensionAsync(fixture, businessClock, actors.SecurityAdmin, account, shop.ShopId);
        await WriteSignalAsync(evidence, "suspended.ready", effectiveAt.ToString("O"), ct);
        var queued = QueuedSale.From(await WaitForPhaseJsonAsync(evidence, "queued", browser.Process, browserReport, ct));
        Assert.True(
            permit.NotBefore < effectiveAt && effectiveAt <= queued.SoldAt && queued.SoldAt < permit.ValidUntil,
            $"Expected {permit.NotBefore:O} < {effectiveAt:O} <= {queued.SoldAt:O} < {permit.ValidUntil:O}.");

        var syncAt = queued.SoldAt.AddSeconds(5);
        SetMerchantAndBusinessClocks(fixture, businessClock, syncAt);
        await WriteSignalAsync(evidence, "reconnect.ready", syncAt.ToString("O"), ct);
        var synced = await WaitForPhaseJsonAsync(evidence, "synced", browser.Process, browserReport, ct);
        await AssertSingleBrowserPassAsync(browser.Process, browserReport);

        var wireSale = AssertWireCarriesQueuedPermit(synced, queued);
        var held = new HeldCase(shop.ShopId, AssertBrowserHeldForReview(synced), queued, permit, syncAt, baseline.Stock, baseline.Sequence);
        var (submission, replayAt) = await AssertReceiptSurvivesReplayRaceAsync(fixture, businessClock, actors, held, wireSale, ct);
        await VerifyReviewAccessAndActionsAsync(fixture, actors, held, wireSale, effectiveAt, ct);
        var deviceRef = await AssertMissedSuspensionProviderViewAsync(fixture, account, shop.ShopId, queued.InvoiceNumber, permit.ValidUntil, replayAt);

        await WriteCorrelatedEvidenceAsync(evidence, "postgres-missed-suspension.json", new
        {
            scenario = "missed-suspension", shopId = shop.ShopId, accountId = account, deviceId = permit.DeviceId, deviceRef,
            clientSaleId = queued.ClientSaleId, invoiceNumber = queued.InvoiceNumber, permitId = permit.PermitId, grantId = permit.GrantId,
            permitNotBefore = permit.NotBefore, effectiveAt, soldAt = queued.SoldAt, permitValidUntil = permit.ValidUntil, syncAt, replayAt,
            submissionId = submission.Id, receivedAt = submission.ReceivedAt, reviewCaseId = held.CaseId,
            rawPayloadSha256 = queued.RawPayloadSha256,
            wireSaleSha256 = synced.GetProperty("wireSaleSha256").GetString(),
            serverPayloadHash = submission.PayloadHash,
            signedPermitSha256 = queued.SignedPermitSha256,
            wireSignedPermitSha256 = SignedPermitSha256(wireSale),
            salesCount = 0,
        }, ct);
    }

    private static async Task AssertNewLeaseRefusedAfterExpiryAsync(
        P0213Fixture fixture, HttpClient client, string deviceId, DateTimeOffset syncAt, CancellationToken ct)
    {
        fixture.Hosts.Fixture.Clock.Now = syncAt;
        fixture.Hosts.StatusClock.Now = syncAt;
        using var refusedReserve = await client.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId, blockSize = 25 }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, refusedReserve.StatusCode);
        Assert.Equal("Licence.Expired", await P0206Fixture.CodeAsync(refusedReserve));
    }

    /// <summary>The upload carries the same signed permit token the browser froze into the queued invoice.</summary>
    private static JsonElement AssertWireCarriesQueuedPermit(JsonElement synced, QueuedSale queued)
    {
        var wireSale = synced.GetProperty("wireSale").Clone();
        Assert.Equal(queued.SignedPermitSha256, SignedPermitSha256(wireSale));
        Assert.Equal(queued.RawPayloadSha256, synced.GetProperty("rawPayloadSha256After").GetString());
        return wireSale;
    }

    /// <summary>The verdict the app stored in IndexedDB from the real upload answer names the posted sale.</summary>
    private static void AssertBrowserSawCreatedSale(JsonElement synced, Guid saleId)
    {
        Assert.Equal((200, "Synced", saleId), (
            synced.GetProperty("httpStatus").GetInt32(),
            synced.GetProperty("queueStatusAfter").GetString(),
            synced.GetProperty("serverSaleId").GetGuid()));
    }

    private static async Task<(SubmissionEvidence Submission, SaleEvidence Sale, string ReplayStatus, DateTimeOffset ReplayAt)> AssertPostedOnceAndReplayAsync(
        P0213Fixture fixture, Merchant shop, HttpClient client, HeldPermit permit, QueuedSale queued,
        DateTimeOffset syncAt, JsonElement wireSale, CancellationToken ct)
    {
        Assert.Equal(1L, await CountMerchantAsync(fixture, "sales", $"shop_id = '{shop.ShopId:D}' AND client_sale_id = '{queued.ClientSaleId}'"));
        var submission = await ReadSubmissionAsync(fixture, shop.ShopId, queued.ClientSaleId);
        var sale = await ReadSaleAsync(fixture, shop.ShopId, queued.ClientSaleId);
        Assert.NotNull(submission);
        Assert.NotNull(sale);
        Assert.Equal((sale!.Id, queued.InvoiceNumber, queued.SoldAt, syncAt, permit.PermitId, permit.GrantId, sale.RequestHash),
            (submission!.SaleId, submission.InvoiceNumber, submission.ClaimedSoldAt, submission.ReceivedAt,
             submission.LicencePermitId, submission.LicenceGrantId, submission.PayloadHash));
        Assert.Equal((queued.InvoiceNumber, queued.SoldAt, 50m, shop.UserId), (sale.InvoiceNumber, sale.SoldAt, sale.TotalAmount, sale.ActorUserId));
        Assert.False(string.IsNullOrWhiteSpace(sale.InvoiceDocumentJson));

        // The replay arrives later than the first receipt, so a rewritten receipt would carry a different received_at.
        var replayAt = syncAt.AddSeconds(ReplayDelaySeconds);
        fixture.Hosts.Fixture.Clock.Now = replayAt;
        fixture.Hosts.StatusClock.Now = replayAt;
        var replay = await SyncSingleAsync(client, permit.DeviceId, wireSale, ct);
        var replayStatus = replay.GetProperty("status").GetString()!;
        Assert.Equal(("duplicate", sale.Id), (replayStatus, replay.GetProperty("saleId").GetGuid()));
        Assert.Equal(1L, await CountMerchantAsync(fixture, "sales", $"shop_id = '{shop.ShopId:D}'"));
        Assert.Equal(1L, await CountMerchantAsync(fixture, "offline_submissions", $"shop_id = '{shop.ShopId:D}'"));
        var afterReplay = await ReadSubmissionAsync(fixture, shop.ShopId, queued.ClientSaleId);
        Assert.Equal((submission.Id, syncAt, submission.PayloadHash, submission.PayloadJson),
            (afterReplay!.Id, afterReplay.ReceivedAt, afterReplay.PayloadHash, afterReplay.PayloadJson));
        return (submission, sale, replayStatus, replayAt);
    }

    private static async Task<string> AssertOrdinaryExpiryProviderViewAsync(
        P0213Fixture fixture, Guid account, Guid shopId, string deviceId, DateTimeOffset termEnd, DateTimeOffset replayAt)
    {
        await DrainBothAsync(fixture);
        using var support = await P0417Tests.SignInAsync(fixture, "support");
        fixture.Hosts.StatusClock.Now = termEnd.AddMicroseconds(-1);
        var beforeEnd = await ReadProviderDevicePermitsAsync(support, account, shopId);
        var deviceBefore = Assert.Single(beforeEnd.GetProperty("devices").EnumerateArray());
        // The provider sees contact times only: the later replay is the device's last contact, not a new receipt.
        Assert.Equal((OfflineDeviceObservationsV1.DeviceRef(shopId, deviceId), termEnd, replayAt, true, "Unknown"), (
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
}
