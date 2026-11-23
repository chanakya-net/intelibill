using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0506LicensingTests
{
    [Fact]
    public async Task Legacy_edge_cases_and_replay_authorization()
    {
        var vectors = LoadLegacyVectors();
        var verifiedAt = At("2027-01-20T11:20:00Z");
        var soldAt7ff1 = At("2027-01-20T11:45:00Z");
        var accessEnd = At("2027-01-20T12:20:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner1 = CreateClient(host);
        var actor = await OpenHistoricalShopWithCustomVerificationAsync(
            owner1, applyAt: verifiedAt, startsAt: verifiedAt.AddDays(-1), endsAt: accessEnd,
            shopVerifiedAt: verifiedAt, accountVerifiedAt: verifiedAt);
        using var otherShopOwner = CreateClient(host);
        await OpenHistoricalShopWithCustomVerificationAsync(
            otherShopOwner, applyAt: verifiedAt, startsAt: verifiedAt.AddDays(-1), endsAt: accessEnd,
            shopVerifiedAt: verifiedAt, accountVerifiedAt: verifiedAt);
        using var owner2 = await CreateSecondSessionClientAsync(host, actor, verifiedAt.AddDays(400));

        // 1. Missing proof on an anchored lease: reserve & stream while shop has single fixture batch,
        //    then omit licenceEvidence on import -> LicenseHistoryReview (EvidenceMissingOnAnchoredLease).
        var anchoredMissing = await BuildIssuedInvoiceAsync(owner1, actor, verifiedAt, accessEnd, "missing", device: "anchored-dev");

        // Seed goods matching unversioned 7ff1de03f vector (0% tax, LEG-7FF1, salesPrice 100, qty 2 -> 200).
        Guid legacyBatchId;
        using (var inboundResp = await owner1.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "Legacy Rice 1kg", barcode = "LEG-7FF1", uom = "pcs", hsnCode = "1006", batchNumber = "LOT-OLD",
            quantity = 10m, totalPurchaseCost = 700m, mrp = 120m, salesPrice = 100m, taxRatePercent = 0m,
            taxIncluded = false, purchaseTaxIncluded = false,
        }, Ct))
        {
            var body = await ReadSuccessAsync(inboundResp);
            legacyBatchId = body.TryGetProperty("inventoryBatchId", out var b) ? b.GetGuid() : body.GetProperty("id").GetGuid();
        }
        await SeedUnanchoredLeaseAsync(actor, LegacyDevice, soldAt7ff1.AddMinutes(-30), verifiedAt.AddDays(3), rangeStart: 1);

        _clock.Now = accessEnd.AddMinutes(10);
        var anchoredImport = await SubmitRecoveryBundleAsync(owner1, actor.Shop, _clock.Now, "p0506-anchored-missing", [anchoredMissing], device: "anchored-dev");
        var anchoredCaseId = anchoredImport.GetProperty("results")[0].GetProperty("caseId").GetGuid();
        var anchoredDetail = await GetCaseDetailAsync(owner1, anchoredCaseId);
        Assert.Equal("LicenseHistoryReview", anchoredDetail.GetProperty("issueType").GetString());
        Assert.Equal("EvidenceMissingOnAnchoredLease", anchoredDetail.GetProperty("licenceHistory").GetProperty("currentSubreason").GetString());
        Assert.Equal(["Escalate"], anchoredDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());

        // 2. Unstamped 7ff1de03f via recovery import: no operator fallback -> Escalated ProvenanceReview + IssuerMissing observation.
        var (unstampedImportBundle, _, _, _) = BuildVectorRecoveryBundle(
            vectors, actor.Shop, LegacyDevice, issuedByUserId: null, legacyBatchId, soldAt7ff1, "A/2627/000005", _clock.Now,
            vectorName: "unversionedPreDocument", prettyWhitespace: false, clientSaleIdOverride: "legacy-unstamped-import");
        var unstampedImport = (await SubmitRawRecoveryBundleAsync(owner1, "p0506-unstamped-import", unstampedImportBundle)).GetProperty("results")[0];
        Assert.Equal("Escalated", unstampedImport.GetProperty("status").GetString());
        var unstampedCaseId = unstampedImport.GetProperty("caseId").GetGuid();
        var unstampedDetail = await GetCaseDetailAsync(owner1, unstampedCaseId);
        Assert.Equal("ProvenanceReview", unstampedDetail.GetProperty("issueType").GetString());
        Assert.Contains("OfflineReconciliation.ProvenanceUnknown",
            unstampedDetail.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()!));
        Assert.Equal("IssuerMissing", unstampedDetail.GetProperty("licenceHistory").GetProperty("currentSubreason").GetString());
        Assert.False(unstampedDetail.GetProperty("licenceHistory").GetProperty("retryEligible").GetBoolean());

        // 3. Unstamped 7ff1de03f via legacy sync: first-sync submitter fallback posts with exact unversioned hash and null FKs!
        var (unstampedSyncBundle, _, _, syncExpectedHash) = BuildVectorRecoveryBundle(
            vectors, actor.Shop, LegacyDevice, issuedByUserId: null, legacyBatchId, soldAt7ff1, "A/2627/000001", _clock.Now,
            vectorName: "unversionedPreDocument", prettyWhitespace: false);
        var unstampedSyncNode = JsonNode.Parse(JsonDocument.Parse(unstampedSyncBundle).RootElement.GetProperty("invoices")[0].GetRawText())!;
        var syncedLegacy = await SyncInvoiceAsync(owner1, unstampedSyncNode, LegacyDevice);
        Assert.Equal("created", syncedLegacy.GetProperty("status").GetString());
        var postedSaleId = syncedLegacy.GetProperty("saleId").GetGuid();

        Guid legacyReceiptId;
        await using (var db = Open(Migrator))
        {
            var posted = await db.Sales.SingleAsync(x => x.Id == postedSaleId, Ct);
            Assert.Equal(("A/2627/000001", syncExpectedHash, soldAt7ff1, 200m),
                (posted.InvoiceNumber, posted.RequestHash, posted.SoldAt, posted.TotalAmount));
            var receipt = await db.OfflineSubmissions.AsNoTracking()
                .SingleAsync(x => x.ShopId == actor.Shop && x.ClientSaleId == "legacy-7ff1-0001", Ct);
            legacyReceiptId = receipt.Id;
            Assert.Null(receipt.LicencePermitId);
            Assert.Null(receipt.LicenceGrantId);
            Assert.Null(receipt.AssertedIssuerUserId);
            Assert.Equal(actor.User, receipt.SubmittedByUserId);
            Assert.Equal(postedSaleId, receipt.SaleId);
        }

        // 4. Exact already-posted match links under CheckHistorySaleAsync with zero economic or sequence changes.
        Guid linkCaseId;
        await using (var db = Open(Migrator))
        {
            var issue = ReconciliationIssue.Create(
                actor.Shop, null, "legacy-7ff1-0001", LegacyDevice, ReconciliationIssueType.LicenseHistoryReview,
                "OfflineLicenceHistory.EvidenceMissing", "Duplicate historical hold on already-posted legacy invoice",
                actor.User, invoiceNumber: "A/2627/000001", offlineSubmissionId: legacyReceiptId);
            db.ReconciliationIssues.Add(issue);
            await db.SaveChangesAsync(Ct);
            linkCaseId = issue.Id;
        }
        var factsBeforeLink = await ReadEconomicFactsAsync(actor.Shop);
        var linkDetail = await GetCaseDetailAsync(owner1, linkCaseId);
        Assert.Equal(["LinkExistingSale", "Escalate"],
            linkDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(postedSaleId, Assert.Single(linkDetail.GetProperty("linkCandidates").EnumerateArray()).GetProperty("operationId").GetGuid());
        using (var linkResp = await owner1.PostAsJsonAsync($"/api/offline-reconciliation/{linkCaseId:D}/resolve", new
        {
            operationId = "p0506-link-exact", expectedVersion = linkDetail.GetProperty("version").GetUInt32(),
            action = "LinkExistingSale", reason = "Exact posted legacy sale matched", linkedOperationId = postedSaleId,
        }, Ct))
        {
            var resolved = await ReadSuccessAsync(linkResp);
            Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
            Assert.Equal(postedSaleId, resolved.GetProperty("saleId").GetGuid());
        }
        Assert.Equal(factsBeforeLink, await ReadEconomicFactsAsync(actor.Shop));

        // 5. Authorized replay paths work first (sync duplicate, bundle AlreadyPosted, operation-receipt replay).
        Assert.Equal("duplicate", (await SyncInvoiceAsync(owner1, unstampedSyncNode.DeepClone(), LegacyDevice)).GetProperty("status").GetString());
        var alreadyPosted = (await SubmitRawRecoveryBundleAsync(owner1, "p0506-replay-bundle", unstampedSyncBundle)).GetProperty("results")[0];
        Assert.Equal("AlreadyPosted", alreadyPosted.GetProperty("status").GetString());
        Assert.Equal(postedSaleId, alreadyPosted.GetProperty("saleId").GetGuid());
        var opReplay = (await SubmitRawRecoveryBundleAsync(owner1, "p0506-replay-bundle", unstampedSyncBundle)).GetProperty("results")[0];
        Assert.Equal("AlreadyPosted", opReplay.GetProperty("status").GetString());

        // 6. Cross-shop isolation: different shop gets zero disclosure / case leakage.
        using (var crossCase = await otherShopOwner.GetAsync($"/api/offline-reconciliation/{anchoredCaseId:D}", Ct))
            Assert.Equal(HttpStatusCode.NotFound, crossCase.StatusCode);
        using (var crossBundle = await PostRawRecoveryBundleAsync(otherShopOwner, "p0506-replay-bundle", unstampedSyncBundle))
            await AssertProblemAsync(crossBundle, HttpStatusCode.BadRequest, "Recovery.BundleShopMismatch");

        // 7. Security suspension blocks sync duplicate, bundle AlreadyPosted, op-receipt replay, and case GET.
        await ApplyAsync(actor.Binding, accessEnd.AddDays(5), revision: 2, restriction: "Security");
        using (var secSync = await owner1.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = LegacyDevice, sales = new[] { unstampedSyncNode } }, Ct))
            await AssertProblemAsync(secSync, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        using (var secBundle = await PostRawRecoveryBundleAsync(owner1, "p0506-replay-bundle", unstampedSyncBundle))
            await AssertProblemAsync(secBundle, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        using (var secGet = await owner1.GetAsync($"/api/offline-reconciliation/{anchoredCaseId:D}", Ct))
            await AssertProblemAsync(secGet, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");

        // Release suspension (rev 3 releasing rev 2) to verify session revocation, role demotion, and membership loss.
        await ApplySuspensionReleaseAsync(actor.Binding, accessEnd.AddDays(10), revision: 3, releasedRevision: 2);

        // 8. Revoked session blocks sync duplicate & bundle op-receipt replay while second session remains authorized.
        await using (var db = Open(Migrator))
            await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET revoked_at = {_clock.Now} WHERE id = {actor.Session}", Ct);
        using (var revSync = await owner1.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = LegacyDevice, sales = new[] { unstampedSyncNode } }, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, revSync.StatusCode);
        using (var revBundle = await PostRawRecoveryBundleAsync(owner1, "p0506-replay-bundle", unstampedSyncBundle))
            Assert.Equal(HttpStatusCode.Unauthorized, revBundle.StatusCode);
        Assert.Equal("AlreadyPosted",
            (await SubmitRawRecoveryBundleAsync(owner2, "p0506-replay-bundle", unstampedSyncBundle)).GetProperty("results")[0].GetProperty("status").GetString());

        // 9. Loss of role (Owner -> Staff) blocks bundle op-receipt replay (stale authority -> 401; fresh Staff token -> 403);
        //    suspended membership blocks sync duplicate too.
        await using (var db = Open(Migrator))
        {
            var m = await db.ShopMemberships.SingleAsync(x => x.ShopId == actor.Shop && x.UserId == actor.User, Ct);
            m.ChangeAuthority(ShopRole.Staff, MembershipStatus.Active);
            await db.SaveChangesAsync(Ct);
        }
        using (var staleOwnerBundle = await PostRawRecoveryBundleAsync(owner2, "p0506-replay-bundle", unstampedSyncBundle))
            Assert.Contains(staleOwnerBundle.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        using var demotedStaffClient = await CreateSecondSessionClientAsync(host, actor, verifiedAt.AddDays(400));
        using (var staffBundle = await PostRawRecoveryBundleAsync(demotedStaffClient, "p0506-replay-bundle", unstampedSyncBundle))
            Assert.Equal(HttpStatusCode.Forbidden, staffBundle.StatusCode);

        await using (var db = Open(Migrator))
        {
            var m = await db.ShopMemberships.SingleAsync(x => x.ShopId == actor.Shop && x.UserId == actor.User, Ct);
            m.ChangeAuthority(ShopRole.Owner, MembershipStatus.Suspended);
            await db.SaveChangesAsync(Ct);
        }
        using (var suspSync = await demotedStaffClient.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = LegacyDevice, sales = new[] { unstampedSyncNode } }, Ct))
            Assert.Contains(suspSync.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });

        Assert.Equal(factsBeforeLink, await ReadEconomicFactsAsync(actor.Shop));
    }

    [Fact]
    public async Task Key_rotation_preserves_archived_key_and_keeps_legacy_receipt_references_null()
    {
        var vectors = LoadLegacyVectors();
        var start = At("2027-01-20T11:20:00Z");
        var soldAt = At("2027-01-20T11:50:00Z");
        var end = At("2027-01-20T12:20:00Z");

        Actor actor;
        JsonNode signedSale;
        Guid legacyBatchId;
        string rawLegacyBundle;
        using (var hostA = CreateHost(key: _keyA))
        {
            await hostA.InitializeAsync();
            using var ownerA = CreateClient(hostA);
            actor = await OpenHistoricalShopWithCustomVerificationAsync(
                ownerA, applyAt: start, startsAt: start.AddDays(-1), endsAt: end,
                shopVerifiedAt: start, accountVerifiedAt: start);
            // Issue one genuine signed permit invoice under keyA on a separate device before adding second batch.
            signedSale = await BuildIssuedInvoiceAsync(ownerA, actor, start, end, "valid", device: "signed-dev");
            legacyBatchId = await ConfigureRegularShopAndSeedVectorGoodsAsync(ownerA, actor.Shop);
            await SeedUnanchoredLeaseAsync(actor, LegacyDevice, soldAt.AddMinutes(-30), start.AddDays(3), rangeStart: 1);
            (rawLegacyBundle, _, _, _) = BuildVectorRecoveryBundle(
                vectors, actor.Shop, LegacyDevice, actor.User, legacyBatchId, soldAt, "A/2627/000002", end.AddMinutes(5), prettyWhitespace: false);
        }

        // Rotate signing key to keyB on a fresh host (same DB; do not re-initialize); advance past ordinary expiry.
        using var rsaB = RSA.Create(2048);
        var keyB = new PermitKey("p0506-key-b", rsaB);
        using var hostB = CreateHost(key: keyB);
        using var ownerB = await CreateSecondSessionClientAsync(hostB, actor, start.AddDays(400));

        _clock.Now = end.AddMinutes(5);
        var syncedSigned = await SyncInvoiceAsync(ownerB, signedSale, device: "signed-dev");
        Assert.Equal("created", syncedSigned.GetProperty("status").GetString());

        var legacyImport = await SubmitRawRecoveryBundleAsync(ownerB, "p0506-rot-legacy", rawLegacyBundle);
        Assert.Equal("ReviewOpened", legacyImport.GetProperty("results")[0].GetProperty("status").GetString());

        await using var db = Open(Migrator);
        var archivedKey = await db.Set<OfflineLicenseVerificationKey>()
            .SingleAsync(k => k.ShopId == actor.Shop && k.KeyId == _keyA.Id, Ct);
        Assert.Equal(_keyA.Fingerprint, archivedKey.Fingerprint);

        var signedReceipt = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop && x.DeviceId == "signed-dev", Ct);
        Assert.NotNull(signedReceipt.LicencePermitId);
        Assert.NotNull(signedReceipt.LicenceGrantId);
        var persistedEvidence = JsonNode.Parse(signedReceipt.PayloadJson)!["licenceEvidence"]!;
        Assert.Equal(_keyA.Id, persistedEvidence["keyId"]!.GetValue<string>());
        Assert.Equal(signedSale["licenceEvidence"]!["signedPermit"]!.GetValue<string>(), persistedEvidence["signedPermit"]!.GetValue<string>());

        var legacyReceipt = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop && x.DeviceId == LegacyDevice, Ct);
        Assert.Null(legacyReceipt.LicencePermitId);
        Assert.Null(legacyReceipt.LicenceGrantId);
        Assert.DoesNotContain("licenceEvidence", legacyReceipt.PayloadJson, StringComparison.Ordinal);
        _output.WriteLine("P0506 key rotation: archivedKeyId={0} fingerprint={1} signedPermitId={2} legacyFKs=null",
            archivedKey.KeyId, archivedKey.Fingerprint, signedReceipt.LicencePermitId);
    }

    private async Task ApplySuspensionReleaseAsync(Binding binding, DateTimeOffset endsAt, long revision, long releasedRevision)
    {
        await using var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var now = _clock.Now;
        var snapshot = new EntitlementSnapshotV1(
            1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request,
            new(revision, now, "Unrestricted", releasedRevision),
            new(revision, now, "Active", binding.Term, "Paid", now.AddDays(-1), endsAt, endsAt, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(
            Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(5),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
    }
}
