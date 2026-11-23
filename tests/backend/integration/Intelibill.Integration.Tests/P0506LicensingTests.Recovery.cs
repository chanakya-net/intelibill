using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0506LicensingTests
{
    [Fact]
    public async Task Proven_legacy_recovery_reuses_the_first_receipt_and_posts_once()
    {
        var vectors = LoadLegacyVectors();
        var verifiedAt = At("2027-01-20T11:20:00Z");
        var soldAt = At("2027-01-20T11:50:00Z");
        var accessEnd = At("2027-01-20T12:20:00Z");
        var importAt = At("2027-01-20T13:00:00Z"); // Past ordinary expiry (accessEnd)

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner1 = CreateClient(host);
        var actor = await OpenHistoricalShopWithCustomVerificationAsync(
            owner1, applyAt: verifiedAt, startsAt: verifiedAt.AddDays(-1), endsAt: accessEnd,
            shopVerifiedAt: verifiedAt, accountVerifiedAt: verifiedAt);
        var batchId = await ConfigureRegularShopAndSeedVectorGoodsAsync(owner1, actor.Shop);
        var lease = await SeedUnanchoredLeaseAsync(actor, LegacyDevice, soldAt.AddMinutes(-50), verifiedAt.AddDays(3), rangeStart: 1);
        using var owner2 = await CreateSecondSessionClientAsync(host, actor, verifiedAt.AddDays(400));

        // 1. Advance past ordinary expiry and prove new offline reservation is refused.
        _clock.Now = importAt;
        await AssertProblemAsync(
            await owner1.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId = LegacyDevice }, Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");

        // 2. Whitespace-sensitive raw bundle: changing one space inside `invoices` invalidates checksum.
        var (rawBundle, invoicesRaw, checksum, expectedHash) = BuildVectorRecoveryBundle(
            vectors, actor.Shop, LegacyDevice, actor.User, batchId, soldAt, "A/2627/000002", importAt, prettyWhitespace: true);
        var tamperedBundle = rawBundle.Replace(invoicesRaw, invoicesRaw.Replace("\n", "\n "), StringComparison.Ordinal);
        using (var badResp = await PostRawRecoveryBundleAsync(owner1, "p0506-bad-ws", tamperedBundle))
            await AssertProblemAsync(badResp, HttpStatusCode.BadRequest, "Recovery.BundleChecksumMismatch");

        // 3. Import valid pre-permit bundle after ordinary expiry -> RecoveryReview with RetryHistoricalLease.
        var factsBefore = await ReadEconomicFactsAsync(actor.Shop);
        int sequenceNextBefore;
        int leaseCountBefore;
        await using (var db = Open(Migrator))
        {
            sequenceNextBefore = await db.InvoiceSequences.Where(s => s.ShopId == actor.Shop).SumAsync(s => s.NextNumber, Ct);
            leaseCountBefore = await db.InvoiceLeases.CountAsync(l => l.ShopId == actor.Shop, Ct);
        }

        var imported = await SubmitRawRecoveryBundleAsync(owner1, "p0506-import-1", rawBundle);
        var row = Assert.Single(imported.GetProperty("results").EnumerateArray());
        Assert.Equal("ReviewOpened", row.GetProperty("status").GetString());
        var caseId = row.GetProperty("caseId").GetGuid();

        Guid firstReceiptId;
        string firstPayloadJson;
        await using (var db = Open(Migrator))
        {
            var receipt = await db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop, Ct);
            firstReceiptId = receipt.Id;
            firstPayloadJson = receipt.PayloadJson;
            Assert.Equal(importAt, receipt.ReceivedAt);
            Assert.Equal(expectedHash, receipt.PayloadHash);
            Assert.Equal(OfflineSubmissionSource.Recovery, receipt.Source);
            Assert.Equal(actor.User, receipt.OriginalIssuerUserId);
            Assert.Equal(lease.Id, receipt.OriginalLeaseId);
            Assert.Null(receipt.LicencePermitId);
            Assert.Null(receipt.LicenceGrantId);
            Assert.Null(receipt.SaleId);
        }

        var detail = await GetCaseDetailAsync(owner1, caseId);
        Assert.Equal("RecoveryReview", detail.GetProperty("issueType").GetString());
        Assert.Contains("RetryHistoricalLease", detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!));
        var caseVersion = detail.GetProperty("version").GetUInt32();

        // 4. Approve via RetryHistoricalLease with returned xmin -> single original-number sale, zero new reservation.
        var resolveOp = "p0506-retry-1";
        using (var resolveResp = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = resolveOp, expectedVersion = caseVersion, action = "RetryHistoricalLease",
            reason = "Contemporaneous commercial coverage verified for pre-permit invoice",
        }, Ct))
        {
            var resolved = await ReadSuccessAsync(resolveResp);
            Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
            Assert.Equal("RetryHistoricalLease", resolved.GetProperty("resolution").GetString());
        }

        // Same operationId replays committed outcome; changed intent fails with Operation.PayloadMismatch.
        using (var replayResp = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = resolveOp, expectedVersion = caseVersion, action = "RetryHistoricalLease",
            reason = "Contemporaneous commercial coverage verified for pre-permit invoice",
        }, Ct))
            Assert.Equal("Resolved", (await ReadSuccessAsync(replayResp)).GetProperty("status").GetString());

        using (var mismatchResp = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
        {
            operationId = resolveOp, expectedVersion = caseVersion, action = "RetryHistoricalLease",
            reason = "Different intent on same key",
        }, Ct))
            await AssertProblemAsync(mismatchResp, HttpStatusCode.Conflict, "Operation.PayloadMismatch");

        await using (var db = Open(Migrator))
        {
            var posted = await db.Sales.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            Assert.Equal("A/2627/000002", posted.InvoiceNumber);
            Assert.Equal(expectedHash, posted.RequestHash);
            Assert.Equal(soldAt, posted.SoldAt);
            Assert.Equal(new DateOnly(2027, 1, 20), posted.BusinessDate);
            Assert.Equal(100m, posted.SubtotalBeforeDiscount);
            Assert.Equal(18m, posted.TotalTaxAmount);
            Assert.Equal(118m, posted.TotalAmount);
            Assert.NotNull(posted.InvoiceDocument);
            Assert.Equal("A/2627/000002", posted.InvoiceDocument!.Number);

            var afterFacts = await ReadEconomicFactsAsync(actor.Shop);
            Assert.Equal(factsBefore.Sales + 1, afterFacts.Sales);
            Assert.Equal(factsBefore.OutStockTransactions + 1, afterFacts.OutStockTransactions);
            Assert.Equal(factsBefore.BatchQuantity - 1m, afterFacts.BatchQuantity);
            Assert.Equal(sequenceNextBefore, await db.InvoiceSequences.Where(s => s.ShopId == actor.Shop).SumAsync(s => s.NextNumber, Ct));
            Assert.Equal(leaseCountBefore, await db.InvoiceLeases.CountAsync(l => l.ShopId == actor.Shop, Ct));

            var retained = await db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == firstReceiptId, Ct);
            Assert.Equal((importAt, firstPayloadJson, expectedHash, OfflineSubmissionSource.Recovery, (Guid?)actor.User, (Guid?)lease.Id),
                (retained.ReceivedAt, retained.PayloadJson, retained.PayloadHash, retained.Source, retained.OriginalIssuerUserId, retained.OriginalLeaseId));
            Assert.Null(retained.LicencePermitId);
            Assert.Null(retained.LicenceGrantId);
            Assert.Equal(posted.Id, retained.SaleId);
            _output.WriteLine(
                "P0506 recovery demo: saleId={0} invoice={1} soldAt={2:O} hash={3} checksum={4} receiptId={5}",
                posted.Id, posted.InvoiceNumber, posted.SoldAt, expectedHash, checksum, firstReceiptId);
        }

        // 5. Concurrent sync vs import race (independent clients/sessions + bounded barrier),
        //    plus deterministic reverse ordering and distinct-hash non-overwrite proof.
        await VerifySyncVsImportRaceAndNonOverwriteAsync(owner1, owner2, actor, batchId, lease.Id, vectors, soldAt, importAt);
    }

    private async Task VerifySyncVsImportRaceAndNonOverwriteAsync(
        HttpClient owner1,
        HttpClient owner2,
        Actor actor,
        Guid batchId,
        Guid leaseId,
        JsonElement vectors,
        DateTimeOffset soldAt,
        DateTimeOffset baseTime)
    {
        var raceAt = baseTime.AddMinutes(5);
        _clock.Now = raceAt;
        var (raceBundle, _, _, raceHash) = BuildVectorRecoveryBundle(
            vectors, actor.Shop, LegacyDevice, actor.User, batchId, soldAt, "A/2627/000003", raceAt,
            prettyWhitespace: false, clientSaleIdOverride: "legacy-race-0003");
        var raceInvoiceNode = JsonNode.Parse(JsonDocument.Parse(raceBundle).RootElement.GetProperty("invoices")[0].GetRawText())!;

        var raceOut = await RunBarrierRaceAsync(
            async () => (Route: "sync", Result: await SyncInvoiceAsync(owner1, raceInvoiceNode.DeepClone(), LegacyDevice)),
            async () => (Route: "import", Result: (await SubmitRawRecoveryBundleAsync(owner2, "p0506-race-op", raceBundle)).GetProperty("results")[0]));
        Assert.Equal(2, raceOut.Length);

        // Snapshot the winning first receipt before retrying either route or replaying the operation key.
        Guid winnerId;
        DateTimeOffset winnerReceivedAt;
        OfflineSubmissionSource winnerSource;
        string winnerJson;
        await using (var db = Open(Migrator))
        {
            var winner = await db.OfflineSubmissions.AsNoTracking()
                .SingleAsync(x => x.ShopId == actor.Shop && x.ClientSaleId == "legacy-race-0003", Ct);
            winnerId = winner.Id;
            winnerReceivedAt = winner.ReceivedAt;
            winnerSource = winner.Source;
            winnerJson = winner.PayloadJson;
            Assert.Equal(raceHash, winner.PayloadHash);
            Assert.Equal(actor.User, winner.OriginalIssuerUserId);
            Assert.Equal(leaseId, winner.OriginalLeaseId);
            Assert.Null(winner.LicencePermitId);
            Assert.Null(winner.LicenceGrantId);
        }

        // Advance clock and repeat BOTH routes + same operation key -> first receipt stays untouched.
        _clock.Now = raceAt.AddMinutes(10);
        await SyncInvoiceAsync(owner1, raceInvoiceNode.DeepClone(), LegacyDevice);
        await SubmitRawRecoveryBundleAsync(owner2, "p0506-race-op", raceBundle);
        await SubmitRawRecoveryBundleAsync(owner2, "p0506-race-op-second", raceBundle);

        // Distinct hash for same (device, clientSaleId) cannot overwrite the first receipt.
        var tampered = raceInvoiceNode.DeepClone();
        tampered["customerName"] = "Tampered Legacy Customer";
        var conflictSync = await SyncInvoiceAsync(owner1, tampered, LegacyDevice);
        Assert.Equal("NeedsReview", conflictSync.GetProperty("status").GetString());

        // Deterministic sync-first then bundle (second invoice A/2627/000004) proves sync wins permanently.
        var syncFirstAt = raceAt.AddMinutes(20);
        _clock.Now = syncFirstAt;
        var (sfBundle, _, _, sfHash) = BuildVectorRecoveryBundle(
            vectors, actor.Shop, LegacyDevice, actor.User, batchId, soldAt, "A/2627/000004", syncFirstAt,
            prettyWhitespace: false, clientSaleIdOverride: "legacy-sync-first-0004");
        var sfNode = JsonNode.Parse(JsonDocument.Parse(sfBundle).RootElement.GetProperty("invoices")[0].GetRawText())!;
        var sfSync = await SyncInvoiceAsync(owner1, sfNode, LegacyDevice);
        Assert.Equal("created", sfSync.GetProperty("status").GetString());
        _clock.Now = syncFirstAt.AddMinutes(5);
        var sfImport = (await SubmitRawRecoveryBundleAsync(owner2, "p0506-sf-op", sfBundle)).GetProperty("results")[0];
        Assert.Equal("AlreadyPosted", sfImport.GetProperty("status").GetString());

        await using (var db = Open(Migrator))
        {
            var afterWinner = await db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == winnerId, Ct);
            Assert.Equal((winnerReceivedAt, winnerSource, winnerJson, raceHash),
                (afterWinner.ReceivedAt, afterWinner.Source, afterWinner.PayloadJson, afterWinner.PayloadHash));
            Assert.Null(afterWinner.LicencePermitId);
            Assert.Null(afterWinner.LicenceGrantId);

            var sfReceipt = await db.OfflineSubmissions.AsNoTracking()
                .SingleAsync(x => x.ShopId == actor.Shop && x.ClientSaleId == "legacy-sync-first-0004", Ct);
            Assert.Equal((syncFirstAt, OfflineSubmissionSource.Sync, sfHash),
                (sfReceipt.ReceivedAt, sfReceipt.Source, sfReceipt.PayloadHash));
            Assert.Null(sfReceipt.LicencePermitId);
            Assert.Null(sfReceipt.LicenceGrantId);
            Assert.NotNull(sfReceipt.SaleId);
        }
    }

    private static Task<HttpResponseMessage> PostRawRecoveryBundleAsync(
        HttpClient client,
        string operationId,
        string rawBundleText)
    {
        var body = "{\"operationId\":" + JsonSerializer.Serialize(operationId) + ",\"bundle\":" + rawBundleText + "}";
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        return client.PostAsync("/api/offline-reconciliation/recovery-submissions", content, Ct);
    }

    private static async Task<T[]> RunBarrierRaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var ready = new CountdownEvent(2);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<T> RunAsync(Func<Task<T>> op)
        {
            ready.Signal();
            await go.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return await op();
        }

        var t1 = Task.Run(() => RunAsync(first));
        var t2 = Task.Run(() => RunAsync(second));
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Race participants failed to reach barrier.");
        go.SetResult();
        return await Task.WhenAll(t1, t2);
    }
}
