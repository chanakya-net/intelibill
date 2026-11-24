using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0505LicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
    : OfflinePermitHttpFixture(fixture, output)
{
    [Fact]
    public async Task Unsupported_history_retains_original_case_and_evidence()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var before = await ReadEconomicFactsAsync(actor.Shop);
        var sale = await BuildIssuedInvoiceAsync(client, actor, start, end, "unsupported");

        var firstReceived = end.AddMinutes(5);
        _clock.Now = firstReceived;
        var first = await SyncInvoiceAsync(client, sale);
        Assert.Equal("NeedsReview", first.GetProperty("status").GetString());
        Assert.Equal("OfflineLicenceHistory.EvidenceUnsupported", first.GetProperty("errors")[0].GetProperty("code").GetString());
        var caseId = first.GetProperty("reviewCaseId").GetGuid();

        _clock.Now = end.AddMinutes(10);
        var second = await SyncInvoiceAsync(client, sale.DeepClone());
        Assert.Equal("NeedsReview", second.GetProperty("status").GetString());
        Assert.Equal("OfflineLicenceHistory.EvidenceUnsupported", second.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(caseId, second.GetProperty("reviewCaseId").GetGuid());

        Assert.Equal(before, await ReadEconomicFactsAsync(actor.Shop));
        await using (var db = Open(Migrator))
        {
            var envelope = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            Assert.Equal(firstReceived, envelope.ReceivedAt);
            Assert.Null(envelope.SaleId);
            var expected = sale.Deserialize<OfflineSaleSyncCommand>(JsonSerializerOptions.Web);
            Assert.True(JsonNode.DeepEquals(
                JsonSerializer.SerializeToNode(expected, JsonSerializerOptions.Web),
                JsonNode.Parse(envelope.PayloadJson)), "Persisted invoice changed.");
            Assert.Equal(1, await db.ReconciliationIssues.CountAsync(x => x.ShopId == actor.Shop, Ct));
        }

        var detail = await GetCaseDetailAsync(client, caseId);
        _output.WriteLine("P0505 detail: issueType={0} codes={1} actions={2}",
            detail.GetProperty("issueType").GetString(),
            detail.GetProperty("reasonCodes"),
            detail.GetProperty("availableActions"));

        Assert.Equal("LicenseHistoryReview", detail.GetProperty("issueType").GetString());
        Assert.Equal(["OfflineLicenceHistory.EvidenceUnsupported"],
            detail.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(["Escalate"],
            detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.True(detail.TryGetProperty("licenceHistory", out var history) && history.ValueKind == JsonValueKind.Object,
            "Detail response missing licenceHistory evidence.");
        Assert.Equal("OfflineLicenceHistory.EvidenceUnsupported", history.GetProperty("currentReasonCode").GetString());
        Assert.False(history.GetProperty("retryEligible").GetBoolean());
        Assert.False(history.GetProperty("linkEligible").GetBoolean());
        Assert.True(history.GetProperty("hasStoredObservations").GetBoolean());
        Assert.Equal(firstReceived, history.GetProperty("firstReceivedAt").GetDateTimeOffset());
        var observations = history.GetProperty("observations").EnumerateArray().ToArray();
        Assert.Single(observations);
        Assert.Equal("OfflineLicenceHistory.EvidenceUnsupported", observations[0].GetProperty("reasonCode").GetString());
        Assert.Equal("version", observations[0].GetProperty("comparedField").GetString());
        Assert.DoesNotContain("signedPermit", detail.GetRawText(), StringComparison.OrdinalIgnoreCase);

        // Compatibility reader: an old #903 ProvenanceReview case with the OfflineLicenceHistory.* prefix
        // remains readable without rewriting its case ID, type, or audit trail, exposing unstored observations as unknown.
        Guid legacyCaseId;
        await using (var db = Open(Migrator))
        {
            var lease = await db.InvoiceLeases.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            var legacySale = sale.DeepClone();
            legacySale["clientSaleId"] = Guid.NewGuid().ToString("N");
            legacySale["invoiceNumber"] = lease.Prefix + (lease.RangeStart + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            var parsedLegacy = legacySale.Deserialize<OfflineSaleSyncCommand>(JsonSerializerOptions.Web)!;
            var legacyHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(actor.Shop, Device, parsedLegacy);
            var legacyEnvelope = OfflineSubmission.Record(
                actor.Shop, Device, parsedLegacy.ClientSaleId, parsedLegacy.InvoiceNumber, legacyHash,
                OfflineSubmissionPayload.Serialize(parsedLegacy), parsedLegacy.SoldAt, firstReceived,
                OfflineSubmissionSource.Sync, actor.User, actor.User, actor.User, lease.Id, null);
            db.OfflineSubmissions.Add(legacyEnvelope);
            var legacyIssue = ReconciliationIssue.Create(
                actor.Shop, null, parsedLegacy.ClientSaleId, Device, ReconciliationIssueType.ProvenanceReview,
                "OfflineLicenceHistory.EvidenceUnsupported", "The original offline licence evidence requires review.",
                actor.User, invoiceNumber: parsedLegacy.InvoiceNumber, offlineSubmissionId: legacyEnvelope.Id);
            db.ReconciliationIssues.Add(legacyIssue);
            await db.SaveChangesAsync(Ct);
            legacyCaseId = legacyIssue.Id;
        }

        var legacyDetail = await GetCaseDetailAsync(client, legacyCaseId);
        Assert.Equal(legacyCaseId, legacyDetail.GetProperty("caseId").GetGuid());
        Assert.Equal("ProvenanceReview", legacyDetail.GetProperty("issueType").GetString());
        Assert.Equal(["Escalate"],
            legacyDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        var legacyHistory = legacyDetail.GetProperty("licenceHistory");
        Assert.Equal("OfflineLicenceHistory.EvidenceUnsupported", legacyHistory.GetProperty("currentReasonCode").GetString());
        Assert.False(legacyHistory.GetProperty("hasStoredObservations").GetBoolean());
        Assert.Empty(legacyHistory.GetProperty("observations").EnumerateArray());
    }

    [Fact]
    public async Task Sync_and_bundle_races_keep_one_case_per_held_envelope()
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner1 = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(owner1, start, end);
        using var owner2 = await CreateSecondSessionClientAsync(host, actor, start.AddDays(400));
        using var staff = await CreateStaffClientAsync(host, actor.Shop, start.AddDays(400));

        // Issue all four offline invoices while the shop is operating within its valid permit window.
        var saleA = await BuildIssuedInvoiceAsync(owner1, actor, start, end, "unsupported");
        var saleB = await BuildIssuedInvoiceAsync(owner1, actor, start, end, "unsupported", device: "p0505-dev-b");
        var saleC = await BuildIssuedInvoiceAsync(owner1, actor, start, end, "unsupported", device: "p0505-dev-c");
        var saleD = await BuildIssuedInvoiceAsync(owner1, actor, start, end, "unsupported", device: "p0505-dev-d");

        // 1. Concurrent sync/sync race on the same held invoice.
        _clock.Now = end.AddMinutes(5);
        var syncRace = await RunBarrierRaceAsync(
            () => SyncInvoiceAsync(owner1, saleA.DeepClone()),
            () => SyncInvoiceAsync(owner2, saleA.DeepClone()));
        Assert.All(syncRace, r => Assert.Equal("NeedsReview", r.GetProperty("status").GetString()));
        var caseA = syncRace[0].GetProperty("reviewCaseId").GetGuid();
        Assert.Equal(caseA, syncRace[1].GetProperty("reviewCaseId").GetGuid());

        // 2. Concurrent bundle/bundle race with different operation keys on a second held invoice.
        var bundleRace = await RunBarrierRaceAsync(
            () => SubmitRecoveryBundleAsync(owner1, actor.Shop, _clock.Now, "bundle-op-1", [saleB], device: "p0505-dev-b"),
            () => SubmitRecoveryBundleAsync(owner2, actor.Shop, _clock.Now, "bundle-op-2", [saleB], device: "p0505-dev-b"));
        var caseB1 = bundleRace[0].GetProperty("results")[0].GetProperty("caseId").GetGuid();
        var caseB2 = bundleRace[1].GetProperty("results")[0].GetProperty("caseId").GetGuid();
        Assert.Equal(caseB1, caseB2);

        // 3. Concurrent sync/bundle race + escalate + advance across lease expiry + actor change.
        var firstReceivedC = end.AddMinutes(6);
        _clock.Now = firstReceivedC;
        var mixedRace = await RunBarrierRaceAsync(
            async () => (await SyncInvoiceAsync(owner1, saleC.DeepClone(), device: "p0505-dev-c")).GetProperty("reviewCaseId").GetGuid(),
            async () => (await SubmitRecoveryBundleAsync(owner2, actor.Shop, _clock.Now, "mixed-op-1", [saleC], device: "p0505-dev-c"))
                .GetProperty("results")[0].GetProperty("caseId").GetGuid());
        var caseC = mixedRace[0];
        Assert.Equal(caseC, mixedRace[1]);

        var detailCBefore = await GetCaseDetailAsync(owner1, caseC);
        var origSourceC = detailCBefore.GetProperty("submission").GetProperty("source").GetString();
        using var escalateResp = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{caseC:D}/resolve", new
        {
            operationId = "esc-c",
            expectedVersion = detailCBefore.GetProperty("version").GetUInt32(),
            action = "Escalate",
            reason = "Escalating held case during race verification",
        }, Ct);
        await ReadSuccessAsync(escalateResp);

        // Advance past 7-day lease expiry and repeat via bundle & staff sync.
        _clock.Now = end.AddDays(9);
        var repeatBundle = await SubmitRecoveryBundleAsync(owner1, actor.Shop, _clock.Now, "mixed-op-2", [saleC], device: "p0505-dev-c");
        Assert.Equal(caseC, repeatBundle.GetProperty("results")[0].GetProperty("caseId").GetGuid());
        Assert.Equal("Escalated", repeatBundle.GetProperty("results")[0].GetProperty("status").GetString());

        // Staff (different actor, not the case creator) resending the same invoice must not receive owner's caseId or open a second case.
        var staffResend = await SyncInvoiceAsync(staff, saleC.DeepClone(), device: "p0505-dev-c");
        Assert.Equal("NeedsReview", staffResend.GetProperty("status").GetString());
        Assert.True(
            !staffResend.TryGetProperty("reviewCaseId", out var hiddenId) || hiddenId.ValueKind == JsonValueKind.Null,
            "Staff caller must not learn another user's case ID.");

        // 4. Reverse order: bundle first (owner), then sync (owner) across lease expiry keeps first Recovery receipt.
        _clock.Now = end.AddMinutes(7);
        var bundleFirst = await SubmitRecoveryBundleAsync(owner1, actor.Shop, _clock.Now, "bundle-first-d", [saleD], device: "p0505-dev-d");
        var caseD = bundleFirst.GetProperty("results")[0].GetProperty("caseId").GetGuid();
        _clock.Now = end.AddDays(10);
        var syncSecond = await SyncInvoiceAsync(owner1, saleD.DeepClone(), device: "p0505-dev-d");
        Assert.Equal(caseD, syncSecond.GetProperty("reviewCaseId").GetGuid());
        var detailD = await GetCaseDetailAsync(owner1, caseD);
        Assert.Equal("Recovery", detailD.GetProperty("submission").GetProperty("source").GetString());
        Assert.Equal(end.AddMinutes(7), detailD.GetProperty("submission").GetProperty("receivedAt").GetDateTimeOffset());

        // 5. Changed hash for saleA gets its own receipt and ConflictingReceipt case referencing saleA's original receipt,
        //    and resending saleA appends a distinct observation and advances caseA's version.
        var versionABefore = (await GetCaseDetailAsync(owner1, caseA)).GetProperty("version").GetUInt32();
        var changedA = saleA.DeepClone();
        changedA["customerName"] = "Tampered payload";
        var conflictResult = await SyncInvoiceAsync(owner1, changedA);
        Assert.Equal("NeedsReview", conflictResult.GetProperty("status").GetString());
        Assert.Equal("OfflineLicenceHistory.ConflictingReceipt", conflictResult.GetProperty("errors")[0].GetProperty("code").GetString());
        var conflictCaseId = conflictResult.GetProperty("reviewCaseId").GetGuid();
        Assert.NotEqual(caseA, conflictCaseId);

        var resendA = await SyncInvoiceAsync(owner1, saleA.DeepClone());
        Assert.Equal(caseA, resendA.GetProperty("reviewCaseId").GetGuid());
        var detailAAfter = await GetCaseDetailAsync(owner1, caseA);
        Assert.True(detailAAfter.GetProperty("version").GetUInt32() != versionABefore,
            "Distinct ConflictingReceipt observation must advance case version.");
        var obsCodesA = detailAAfter.GetProperty("licenceHistory").GetProperty("observations")
            .EnumerateArray().Select(o => o.GetProperty("reasonCode").GetString()).ToArray();
        Assert.Contains("OfflineLicenceHistory.EvidenceUnsupported", obsCodesA);
        Assert.Contains("OfflineLicenceHistory.ConflictingReceipt", obsCodesA);

        await using var db = Open(Migrator);
        Assert.Equal(origSourceC, (await GetCaseDetailAsync(owner1, caseC)).GetProperty("submission").GetProperty("source").GetString());
        Assert.Equal(5, await db.OfflineSubmissions.CountAsync(x => x.ShopId == actor.Shop, Ct));
        Assert.Equal(5, await db.ReconciliationIssues.CountAsync(x => x.ShopId == actor.Shop, Ct));
        Assert.False(await db.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
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
