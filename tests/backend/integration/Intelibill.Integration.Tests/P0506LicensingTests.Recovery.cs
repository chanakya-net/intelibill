using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0506LicensingTests
{
    [Fact]
    public async Task Proven_legacy_recovery_reuses_the_first_receipt_and_posts_once()
    {
        var vectors = LoadLegacyVectors();
        var v6 = vectors.GetProperty("v1FrozenDocument");
        var literalBundle = v6.GetProperty("exportedBundleText").GetString()!;
        var pinnedHash = v6.GetProperty("expectedServerPayloadHash").GetString()!;
        var verifiedAt = At("2027-01-20T11:20:00Z");
        var importAt = At("2027-01-20T13:00:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenPinnedVectorShopAsync(
            host, owner, vectors, verifiedAt, verifiedAt.AddDays(-1), At("2027-01-20T12:20:00Z"), verifiedAt, verifiedAt);

        _clock.Now = importAt;
        await AssertProblemAsync(
            await owner.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId = LegacyDevice }, Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");

        var before = await ReadEconomicFactsAsync(actor.Shop);
        var (seqBefore, leaseCountBefore) = await ReadSequenceAndLeaseCountsAsync(actor.Shop);

        var imported = await SubmitRawRecoveryBundleAsync(owner, "p0506-literal-import", literalBundle);
        var caseId = Assert.Single(imported.GetProperty("results").EnumerateArray()).GetProperty("caseId").GetGuid();
        var (firstReceiptId, firstJson) = await SnapshotFirstReceiptAsync(actor.Shop, importAt, pinnedHash, OfflineSubmissionSource.Recovery);
        var detail = await GetCaseDetailAsync(owner, caseId);
        Assert.Equal("RecoveryReview", detail.GetProperty("issueType").GetString());
        var ver = detail.GetProperty("version").GetUInt32();
        await ChangeLiveSellerDetailsAsync(actor.Shop);

        var resolved = await ResolveCaseAsync(owner, caseId, ver, "p0506-retry-1", "RetryHistoricalLease", "Contemporaneous coverage verified");
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        await AssertPostedLiteralV1SaleAsync(actor.Shop, firstReceiptId, firstJson, importAt, pinnedHash, before, seqBefore, leaseCountBefore);
        Assert.Equal("Resolved", (await ResolveCaseAsync(owner, caseId, ver, "p0506-retry-1", "RetryHistoricalLease", "Contemporaneous coverage verified")).GetProperty("status").GetString());
        using (var mismatch = await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new { operationId = "p0506-retry-1", expectedVersion = ver, action = "RetryHistoricalLease", reason = "Changed intent" }, Ct))
            await AssertProblemAsync(mismatch, HttpStatusCode.Conflict, "Operation.PayloadMismatch");

        await AssertPostedLiteralV1SaleAsync(actor.Shop, firstReceiptId, firstJson, importAt, pinnedHash, before, seqBefore, leaseCountBefore);
    }

    [Fact]
    public async Task Synthetic_whitespace_control_enforces_exact_raw_array_checksum()
    {
        var vectors = LoadLegacyVectors();
        var wsControl = vectors.GetProperty("syntheticPrettyWhitespaceControl");
        var bundleText = wsControl.GetProperty("bundleText").GetString()!;
        var rawInvoices = wsControl.GetProperty("invoicesRawJson").GetString()!;
        var pinnedHash = wsControl.GetProperty("expectedServerPayloadHash").GetString()!;
        var verifiedAt = At("2027-01-20T11:20:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenPinnedVectorShopAsync(
            host, owner, vectors, verifiedAt, verifiedAt.AddDays(-1), At("2027-01-20T12:20:00Z"), verifiedAt, verifiedAt);

        _clock.Now = At("2027-01-20T13:00:00Z");
        var tampered = bundleText.Replace(rawInvoices, rawInvoices.Replace("\n", "\n "), StringComparison.Ordinal);
        using (var badResp = await PostRawRecoveryBundleAsync(owner, "p0506-bad-ws", tampered))
            await AssertProblemAsync(badResp, HttpStatusCode.BadRequest, "Recovery.BundleChecksumMismatch");

        var imported = await SubmitRawRecoveryBundleAsync(owner, "p0506-good-ws", bundleText);
        Assert.Equal("ReviewOpened", Assert.Single(imported.GetProperty("results").EnumerateArray()).GetProperty("status").GetString());
        await AssertRetainedUnpostedReceiptAsync(actor.Shop, _clock.Now, pinnedHash, "A/2627/000002");
    }

    [Fact]
    public async Task Import_first_then_sync_preserves_pending_recovery_review_until_approved()
    {
        var vectors = LoadLegacyVectors();
        var v6 = vectors.GetProperty("v1FrozenDocument");
        var literalBundle = v6.GetProperty("exportedBundleText").GetString()!;
        var literalSyncNode = JsonNode.Parse(v6.GetProperty("expectedSyncRequestJson").GetString()!)!;
        var pinnedHash = v6.GetProperty("expectedServerPayloadHash").GetString()!;
        var verifiedAt = At("2027-01-20T11:20:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenPinnedVectorShopAsync(
            host, owner, vectors, verifiedAt, verifiedAt.AddDays(-1), At("2027-01-20T12:20:00Z"), verifiedAt, verifiedAt);
        var before = await ReadEconomicFactsAsync(actor.Shop);
        var (seqBefore, leaseCountBefore) = await ReadSequenceAndLeaseCountsAsync(actor.Shop);

        _clock.Now = At("2027-01-20T12:05:00Z");
        var imported = await SubmitRawRecoveryBundleAsync(owner, "p0506-if-op", literalBundle);
        var caseId = imported.GetProperty("results")[0].GetProperty("caseId").GetGuid();
        var (firstReceiptId, firstJson) = await SnapshotFirstReceiptAsync(actor.Shop, _clock.Now, pinnedHash, OfflineSubmissionSource.Recovery);

        _clock.Now = At("2027-01-20T12:10:00Z");
        var synced = await SyncInvoiceAsync(owner, literalSyncNode.DeepClone(), LegacyDevice);
        Assert.Equal("NeedsReview", synced.GetProperty("status").GetString());
        Assert.Equal(caseId, synced.GetProperty("reviewCaseId").GetGuid());
        Assert.Equal(before, await ReadEconomicFactsAsync(actor.Shop));

        var detail = await GetCaseDetailAsync(owner, caseId);
        await ResolveCaseAsync(owner, caseId, detail.GetProperty("version").GetUInt32(), "p0506-if-approve", "RetryHistoricalLease", "Approved after reconnect");
        await AssertPostedLiteralV1SaleAsync(actor.Shop, firstReceiptId, firstJson, At("2027-01-20T12:05:00Z"), pinnedHash, before, seqBefore, leaseCountBefore);
        Assert.Equal("duplicate", (await SyncInvoiceAsync(owner, literalSyncNode.DeepClone(), LegacyDevice)).GetProperty("status").GetString());

        // Distinct hash for same (device, clientSaleId) cannot overwrite the first receipt or post a second sale.
        var tampered = literalSyncNode.DeepClone();
        tampered["customerName"] = "Tampered Legacy Customer";
        Assert.Equal("NeedsReview", (await SyncInvoiceAsync(owner, tampered, LegacyDevice)).GetProperty("status").GetString());
        await AssertPostedLiteralV1SaleAsync(actor.Shop, firstReceiptId, firstJson, At("2027-01-20T12:05:00Z"), pinnedHash, before, seqBefore, leaseCountBefore);
    }

    [Fact]
    public async Task Sync_first_then_import_and_barrier_race_post_at_most_once()
    {
        var vectors = LoadLegacyVectors();
        var v6 = vectors.GetProperty("v1FrozenDocument");
        var literalBundle = v6.GetProperty("exportedBundleText").GetString()!;
        var literalSyncNode = JsonNode.Parse(v6.GetProperty("expectedSyncRequestJson").GetString()!)!;
        var pinnedHash = v6.GetProperty("expectedServerPayloadHash").GetString()!;
        var verifiedAt = At("2027-01-20T11:20:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner1 = CreateClient(host);
        var actor = await OpenPinnedVectorShopAsync(
            host, owner1, vectors, verifiedAt, verifiedAt.AddDays(-1), At("2027-01-20T12:20:00Z"), verifiedAt, verifiedAt);
        using var owner2 = await CreateSecondSessionClientAsync(host, actor, verifiedAt.AddDays(400));

        // 1. Deterministic sync-first then import on literal pinned invoice A/2627/000002.
        var beforeSyncFirst = await ReadEconomicFactsAsync(actor.Shop);
        _clock.Now = At("2027-01-20T12:05:00Z");
        var sfSync = await SyncInvoiceAsync(owner1, literalSyncNode.DeepClone(), LegacyDevice);
        Assert.Equal("created", sfSync.GetProperty("status").GetString());
        var sfSaleId = sfSync.GetProperty("saleId").GetGuid();
        var sfImport = (await SubmitRawRecoveryBundleAsync(owner2, "p0506-sf-op", literalBundle)).GetProperty("results")[0];
        Assert.Equal("AlreadyPosted", sfImport.GetProperty("status").GetString());
        Assert.Equal(sfSaleId, sfImport.GetProperty("saleId").GetGuid());
        Assert.Equal(beforeSyncFirst.Sales + 1, (await ReadEconomicFactsAsync(actor.Shop)).Sales);

        // 2. Simultaneous barrier race on a second invoice (A/2627/000003) inspecting BOTH outcomes.
        await VerifyBarrierRaceOutcomesAsync(owner1, owner2, actor, literalSyncNode);
    }

    [Fact]
    public async Task Corrupted_retained_jsonb_holds_without_breaking_batch_or_case_detail()
    {
        var vectors = LoadLegacyVectors();
        var v6 = vectors.GetProperty("v1FrozenDocument");
        var validNode = JsonNode.Parse(v6.GetProperty("expectedSyncRequestJson").GetString()!)!.AsObject();
        var verifiedAt = At("2027-01-20T11:20:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenPinnedVectorShopAsync(
            host, owner, vectors, verifiedAt, verifiedAt.AddDays(-1), At("2027-01-20T12:20:00Z"), verifiedAt, verifiedAt);

        var shapes = new[] { ("corrupt-empty", "A/2627/000010", "{}"), ("corrupt-null-items", "A/2627/000011", "{\"items\":null}"), ("corrupt-null-line", "A/2627/000012", "{\"items\":[null]}") };
        var batchNodes = shapes.Select(s => (JsonNode)CloneWithIdentity(validNode, s.Item1, s.Item2)).Append(validNode.DeepClone()).ToArray();
        await using (var db = Open(Migrator))
        {
            for (var i = 0; i < shapes.Length; i++)
            {
                var (cid, inv, json) = shapes[i];
                var cmd = JsonSerializer.Deserialize<Intelibill.Application.Features.Sales.Commands.SyncOfflineSales.OfflineSaleSyncCommand>(
                    batchNodes[i].ToJsonString(), JsonSerializerOptions.Web)!;
                var hash = Intelibill.Application.Features.Sales.Commands.SyncOfflineSales.OfflineSaleSyncIdempotencyHasher.ComputeHash(actor.Shop, LegacyDevice, cmd);
                db.OfflineSubmissions.Add(OfflineSubmission.Record(actor.Shop, LegacyDevice, cid, inv, hash, json, At("2027-01-20T11:50:00Z"), verifiedAt, OfflineSubmissionSource.Recovery, actor.User, actor.User, actor.User, null, "seed"));
            }
            await db.SaveChangesAsync(Ct);
        }

        _clock.Now = At("2027-01-20T12:15:00Z");
        var batchResp = await SubmitRawRecoveryBundleAsync(owner, "p0506-corrupt-batch", BuildCompactBundle(actor.Shop, LegacyDevice, _clock.Now, batchNodes));
        var results = batchResp.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(4, results.Length);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("ReviewOpened", results[i].GetProperty("status").GetString());
            var detail = await GetCaseDetailAsync(owner, results[i].GetProperty("caseId").GetGuid());
            Assert.Equal("LicenseHistoryReview", detail.GetProperty("issueType").GetString());
            Assert.Equal("PayloadCorrupted", detail.GetProperty("licenceHistory").GetProperty("currentSubreason").GetString());
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("submission").ValueKind);
        }

        Assert.Equal("ReviewOpened", results[3].GetProperty("status").GetString());
        var validDetail = await GetCaseDetailAsync(owner, results[3].GetProperty("caseId").GetGuid());
        Assert.Equal("RecoveryReview", validDetail.GetProperty("issueType").GetString());
    }

    private async Task VerifyBarrierRaceOutcomesAsync(HttpClient owner1, HttpClient owner2, Actor actor, JsonNode templateNode)
    {
        var raceNode = CloneWithIdentity(templateNode, "legacy-race-0003", "A/2627/000003");
        var raceAt = At("2027-01-20T12:10:00Z");
        _clock.Now = raceAt;
        var raceBundle = BuildCompactBundle(actor.Shop, LegacyDevice, raceAt, raceNode);
        var before = await ReadEconomicFactsAsync(actor.Shop);

        var outcomes = await RunBarrierRaceAsync(
            async () => (Route: "sync", Body: await SyncInvoiceAsync(owner1, raceNode.DeepClone(), LegacyDevice)),
            async () => (Route: "import", Body: (await SubmitRawRecoveryBundleAsync(owner2, "p0506-race-op", raceBundle)).GetProperty("results")[0]));
        var syncRes = outcomes.Single(o => o.Route == "sync").Body;
        var importRes = outcomes.Single(o => o.Route == "import").Body;

        await using var db = Open(Migrator);
        var winner = await db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop && x.ClientSaleId == "legacy-race-0003", Ct);
        if (importRes.GetProperty("status").GetString() == "AlreadyPosted")
        {
            Assert.Equal(OfflineSubmissionSource.Sync, winner.Source);
            Assert.Equal("created", syncRes.GetProperty("status").GetString());
            Assert.Equal(syncRes.GetProperty("saleId").GetGuid(), importRes.GetProperty("saleId").GetGuid());
            Assert.Equal(before.Sales + 1, (await ReadEconomicFactsAsync(actor.Shop)).Sales);
        }
        else
        {
            Assert.Equal("ReviewOpened", importRes.GetProperty("status").GetString());
            Assert.Equal("NeedsReview", syncRes.GetProperty("status").GetString());
            var caseId = importRes.GetProperty("caseId").GetGuid();
            Assert.Equal(caseId, syncRes.GetProperty("reviewCaseId").GetGuid());
            Assert.Equal(before, await ReadEconomicFactsAsync(actor.Shop));
            var detail = await GetCaseDetailAsync(owner1, caseId);
            await ResolveCaseAsync(owner1, caseId, detail.GetProperty("version").GetUInt32(), "p0506-race-approve", "RetryHistoricalLease", "Race winner approved");
            Assert.Equal(before.Sales + 1, (await ReadEconomicFactsAsync(actor.Shop)).Sales);
        }

        Assert.Equal("duplicate", (await SyncInvoiceAsync(owner1, raceNode.DeepClone(), LegacyDevice)).GetProperty("status").GetString());
        Assert.Equal("AlreadyPosted", (await SubmitRawRecoveryBundleAsync(owner2, "p0506-race-op-replay", raceBundle)).GetProperty("results")[0].GetProperty("status").GetString());
        Assert.Equal(before.Sales + 1, (await ReadEconomicFactsAsync(actor.Shop)).Sales);
    }

    private async Task<(int SeqNext, int Leases)> ReadSequenceAndLeaseCountsAsync(Guid shopId)
    {
        await using var db = Open(Migrator);
        return (await db.InvoiceSequences.Where(s => s.ShopId == shopId).SumAsync(s => s.NextNumber, Ct),
            await db.InvoiceLeases.CountAsync(l => l.ShopId == shopId, Ct));
    }

    private async Task<(Guid Id, string PayloadJson)> SnapshotFirstReceiptAsync(Guid shopId, DateTimeOffset receivedAt, string expectedHash, OfflineSubmissionSource source)
    {
        await using var db = Open(Migrator);
        var receipt = await db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == shopId, Ct);
        Assert.Equal((receivedAt, expectedHash, source), (receipt.ReceivedAt, receipt.PayloadHash, receipt.Source));
        Assert.Null(receipt.LicencePermitId);
        Assert.Null(receipt.LicenceGrantId);
        Assert.Null(receipt.SaleId);
        return (receipt.Id, receipt.PayloadJson);
    }

    private async Task AssertPostedLiteralV1SaleAsync(Guid shopId, Guid firstReceiptId, string firstJson, DateTimeOffset importAt, string pinnedHash, ShopEconomicFacts before, int seqBefore, int leasesBefore)
    {
        await using var db = Open(Migrator);
        var posted = await db.Sales.SingleAsync(x => x.ShopId == shopId, Ct);
        Assert.Equal(("A/2627/000002", pinnedHash, At("2027-01-20T11:50:00Z"), new DateOnly(2027, 1, 20), 100m, 18m, 118m),
            (posted.InvoiceNumber, posted.RequestHash, posted.SoldAt, posted.BusinessDate, posted.SubtotalBeforeDiscount, posted.TotalTaxAmount, posted.TotalAmount));
        AssertArchivedSellerAndNumbering(posted.InvoiceDocument!);

        var after = await ReadEconomicFactsAsync(shopId);
        Assert.Equal((before.Sales + 1, before.OutStockTransactions + 1, before.BatchQuantity - 1m), (after.Sales, after.OutStockTransactions, after.BatchQuantity));
        Assert.Equal((seqBefore, leasesBefore), await ReadSequenceAndLeaseCountsAsync(shopId));

        var retained = await db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.Id == firstReceiptId, Ct);
        Assert.Equal((importAt, firstJson, pinnedHash, (Guid?)posted.Id), (retained.ReceivedAt, retained.PayloadJson, retained.PayloadHash, retained.SaleId));
        Assert.Null(retained.LicencePermitId);
        Assert.Null(retained.LicenceGrantId);
        _output.WriteLine("P0506 literal recovery demo: saleId={0} invoice={1} soldAt={2:O} hash={3} receiptId={4}", posted.Id, posted.InvoiceNumber, posted.SoldAt, pinnedHash, firstReceiptId);
    }

    private static JsonObject CloneWithIdentity(JsonNode template, string clientSaleId, string invoiceNumber)
    {
        var copy = template.DeepClone().AsObject();
        copy["clientSaleId"] = clientSaleId;
        copy["invoiceNumber"] = invoiceNumber;
        return copy;
    }
}
