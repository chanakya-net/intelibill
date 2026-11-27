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
    public async Task Anchored_missing_proof_and_7ff1_missing_reserver_hold_for_review()
    {
        var vectors = LoadLegacyVectors();
        var v7 = vectors.GetProperty("unversionedPreDocument");
        var verifiedAt = At("2027-01-20T11:20:00Z");
        var accessEnd = At("2027-01-20T12:20:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenPinnedVectorShopAsync(
            host, owner, vectors, verifiedAt, verifiedAt.AddDays(-1), accessEnd, verifiedAt, verifiedAt,
            include671dLease: false, include7ff1Lease: true, seed7ff1Batch: true, seed671dBatch: false);

        // 1. Anchored lease missing licenceEvidence -> EvidenceMissingOnAnchoredLease.
        var anchoredMissing = await BuildIssuedInvoiceAsync(owner, actor, verifiedAt, accessEnd, "missing", device: "anchored-dev");
        _clock.Now = accessEnd.AddMinutes(10);
        var anchoredImport = await SubmitRecoveryBundleAsync(owner, actor.Shop, _clock.Now, "p0506-anchored-missing", [anchoredMissing], device: "anchored-dev");
        var anchoredDetail = await GetCaseDetailAsync(owner, anchoredImport.GetProperty("results")[0].GetProperty("caseId").GetGuid());
        Assert.Equal(("LicenseHistoryReview", "EvidenceMissingOnAnchoredLease"),
            (anchoredDetail.GetProperty("issueType").GetString(), anchoredDetail.GetProperty("licenceHistory").GetProperty("currentSubreason").GetString()));
        Assert.Equal(["Escalate"], anchoredDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());

        // 2. Literal 7ff1de03f invoice (INV-2026-27-000001) on authentic 7ff1de03f lease (null ReservedByUserId):
        //    - Recovery import -> Escalated ProvenanceReview + IssuerMissing.
        var v7SyncNode = JsonNode.Parse(v7.GetProperty("expectedSyncRequestJson").GetString()!)!;
        var importNode = CloneWithIdentity(v7SyncNode, "legacy-7ff1-import", "INV-2026-27-000005");
        var unstampedImport = (await SubmitRawRecoveryBundleAsync(owner, "p0506-7ff1-import", BuildCompactBundle(actor.Shop, LegacyDevice, _clock.Now, importNode))).GetProperty("results")[0];
        Assert.Equal("Escalated", unstampedImport.GetProperty("status").GetString());
        var unstampedDetail = await GetCaseDetailAsync(owner, unstampedImport.GetProperty("caseId").GetGuid());
        Assert.Equal(("ProvenanceReview", "IssuerMissing"),
            (unstampedDetail.GetProperty("issueType").GetString(), unstampedDetail.GetProperty("licenceHistory").GetProperty("currentSubreason").GetString()));

        //    - Sync on authentic 7ff1de03f lease (null ReservedByUserId) -> LicenseHistoryReview / LeaseReserverUnproven!
        var beforeSync = await ReadEconomicFactsAsync(actor.Shop);
        var held7ff1 = await SyncInvoiceAsync(owner, AsCurrentInvoice(v7SyncNode, actor.User), LegacyDevice);
        Assert.Equal("NeedsReview", held7ff1.GetProperty("status").GetString());
        var held7ff1Detail = await GetCaseDetailAsync(owner, held7ff1.GetProperty("reviewCaseId").GetGuid());
        Assert.Equal(("LicenseHistoryReview", "LeaseReserverUnproven"),
            (held7ff1Detail.GetProperty("issueType").GetString(), held7ff1Detail.GetProperty("licenceHistory").GetProperty("currentSubreason").GetString()));
        Assert.Equal(["Escalate"], held7ff1Detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(beforeSync, await ReadEconomicFactsAsync(actor.Shop));

        // 3. Synthetic post-reserver/pre-permit control: unanchored lease WITH ReservedByUserId allows first-sync fallback + exact LinkExistingSale.
        await VerifySyntheticPostReserverSyncAndLinkAsync(owner, actor, v7SyncNode, verifiedAt);
    }

    [Fact]
    public async Task Replay_authorization_blocks_cross_shop_suspension_revocation_and_role_loss()
    {
        var vectors = LoadLegacyVectors();
        var v6 = vectors.GetProperty("v1FrozenDocument");
        var literalBundle = v6.GetProperty("exportedBundleText").GetString()!;
        var literalSyncNode = JsonNode.Parse(v6.GetProperty("expectedSyncRequestJson").GetString()!)!;
        var verifiedAt = At("2027-01-20T11:20:00Z");
        var accessEnd = At("2027-01-20T12:20:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner1 = CreateClient(host);
        var actor = await OpenPinnedVectorShopAsync(
            host, owner1, vectors, verifiedAt, verifiedAt.AddDays(-1), accessEnd, verifiedAt, verifiedAt);
        using var otherShopOwner = CreateClient(host);
        await OpenHistoricalShopWithCustomVerificationAsync(otherShopOwner, verifiedAt, verifiedAt.AddDays(-1), accessEnd, verifiedAt, verifiedAt);
        using var owner2 = await CreateSecondSessionClientAsync(host, actor, verifiedAt.AddDays(400));

        _clock.Now = At("2027-01-20T12:05:00Z");
        var postedSaleId = (await SyncInvoiceAsync(owner1, literalSyncNode.DeepClone(), LegacyDevice)).GetProperty("saleId").GetGuid();
        var factsAfterPost = await ReadEconomicFactsAsync(actor.Shop);

        // Authorized replay works first (sync duplicate, bundle AlreadyPosted, op-receipt replay).
        Assert.Equal("duplicate", (await SyncInvoiceAsync(owner1, literalSyncNode.DeepClone(), LegacyDevice)).GetProperty("status").GetString());
        Assert.Equal(postedSaleId, (await SubmitRawRecoveryBundleAsync(owner1, "p0506-replay-op", literalBundle)).GetProperty("results")[0].GetProperty("saleId").GetGuid());
        Assert.Equal("AlreadyPosted", (await SubmitRawRecoveryBundleAsync(owner1, "p0506-replay-op", literalBundle)).GetProperty("results")[0].GetProperty("status").GetString());

        // Cross-shop refusal + security suspension refusal.
        using (var crossBundle = await PostRawRecoveryBundleAsync(otherShopOwner, "p0506-replay-op", literalBundle))
            await AssertProblemAsync(crossBundle, HttpStatusCode.BadRequest, "Recovery.BundleShopMismatch");

        await ApplyAsync(actor.Binding, accessEnd.AddDays(5), revision: 2, restriction: "Security");
        using (var secSync = await owner1.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = LegacyDevice, sales = new[] { literalSyncNode } }, Ct))
            await AssertProblemAsync(secSync, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        using (var secBundle = await PostRawRecoveryBundleAsync(owner1, "p0506-replay-op", literalBundle))
            await AssertProblemAsync(secBundle, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");

        await ApplySuspensionReleaseAsync(actor.Binding, accessEnd.AddDays(10), revision: 3, releasedRevision: 2);
        await AssertRevocationAndRoleLossRefusalsAsync(host, owner1, owner2, actor, literalSyncNode, literalBundle);
        Assert.Equal(factsAfterPost, await ReadEconomicFactsAsync(actor.Shop));
    }

    [Fact]
    public async Task Key_rotation_preserves_archived_key_and_keeps_legacy_receipt_references_null()
    {
        var vectors = LoadLegacyVectors();
        var literalBundle = vectors.GetProperty("v1FrozenDocument").GetProperty("exportedBundleText").GetString()!;
        var start = At("2027-01-20T11:20:00Z");
        var end = At("2027-01-20T12:20:00Z");

        Actor actor;
        JsonNode signedSale;
        using (var hostA = CreateHost(key: _keyA))
        {
            await hostA.InitializeAsync();
            using var ownerA = CreateClient(hostA);
            actor = await OpenPinnedVectorShopAsync(hostA, ownerA, vectors, start, start.AddDays(-1), end, start, start, seed7ff1Batch: false, seed671dBatch: true);
            signedSale = await BuildIssuedInvoiceAsync(ownerA, actor, start, end, "valid", device: "signed-dev");
        }

        using var rsaB = RSA.Create(2048);
        using var hostB = CreateHost(key: new PermitKey("p0506-key-b", rsaB));
        using var ownerB = await CreateSecondSessionClientAsync(hostB, actor, start.AddDays(400));

        _clock.Now = end.AddMinutes(5);
        var signedSyncStatus = (await SyncInvoiceAsync(ownerB, signedSale, device: "signed-dev")).GetProperty("status").GetString();
        Assert.True(signedSyncStatus is "created" or "SyncedWithWarnings", signedSyncStatus);
        Assert.Equal("ReviewOpened", (await SubmitRawRecoveryBundleAsync(ownerB, "p0506-rot-legacy", literalBundle)).GetProperty("results")[0].GetProperty("status").GetString());

        await using var db = Open(Migrator);
        var archivedKey = await db.Set<OfflineLicenseVerificationKey>().SingleAsync(k => k.ShopId == actor.Shop && k.KeyId == _keyA.Id, Ct);
        Assert.Equal(_keyA.Fingerprint, archivedKey.Fingerprint);
        var signedReceipt = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop && x.DeviceId == "signed-dev", Ct);
        Assert.NotNull(signedReceipt.LicencePermitId);
        Assert.NotNull(signedReceipt.LicenceGrantId);
        var legacyReceipt = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop && x.DeviceId == LegacyDevice, Ct);
        Assert.Null(legacyReceipt.LicencePermitId);
        Assert.Null(legacyReceipt.LicenceGrantId);
        Assert.DoesNotContain("licenceEvidence", legacyReceipt.PayloadJson, StringComparison.Ordinal);
        _output.WriteLine("P0506 key rotation: archivedKeyId={0} fingerprint={1} signedPermitId={2} legacyFKs=null",
            archivedKey.KeyId, archivedKey.Fingerprint, signedReceipt.LicencePermitId);
    }

    private async Task VerifySyntheticPostReserverSyncAndLinkAsync(HttpClient owner, Actor actor, JsonNode v7SyncNode, DateTimeOffset verifiedAt)
    {
        await SeedUnanchoredLeaseAsync(actor, "post-reserver-dev", At("2027-01-20T11:00:00Z"), verifiedAt.AddDays(3), 50, "INV-2026-27-", withReserver: true);
        var controlNode = AsCurrentInvoice(CloneWithIdentity(v7SyncNode, "synthetic-post-reserver-0001", "INV-2026-27-000050"), actor.User);
        var synced = await SyncInvoiceAsync(owner, controlNode, "post-reserver-dev");
        Assert.Equal("created", synced.GetProperty("status").GetString());
        var saleId = synced.GetProperty("saleId").GetGuid();

        Guid caseId;
        await using (var db = Open(Migrator))
        {
            var receipt = await db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop && x.ClientSaleId == "synthetic-post-reserver-0001", Ct);
            Assert.Null(receipt.LicencePermitId);
            Assert.Null(receipt.LicenceGrantId);
            var issue = ReconciliationIssue.Create(actor.Shop, null, receipt.ClientSaleId, receipt.DeviceId, ReconciliationIssueType.LicenseHistoryReview,
                "OfflineLicenceHistory.EvidenceMissing", "Duplicate historical hold on already-posted synthetic control", actor.User, invoiceNumber: receipt.InvoiceNumber, offlineSubmissionId: receipt.Id);
            db.ReconciliationIssues.Add(issue);
            await db.SaveChangesAsync(Ct);
            caseId = issue.Id;
        }

        var beforeLink = await ReadEconomicFactsAsync(actor.Shop);
        var detail = await GetCaseDetailAsync(owner, caseId);
        Assert.Equal(["LinkExistingSale", "Escalate"], detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        var resolved = await ResolveCaseAsync(owner, caseId, detail.GetProperty("version").GetUInt32(), "p0506-link-exact", "LinkExistingSale", "Exact posted sale matched", saleId);
        Assert.Equal(("Resolved", saleId), (resolved.GetProperty("status").GetString(), resolved.GetProperty("saleId").GetGuid()));
        Assert.Equal(beforeLink, await ReadEconomicFactsAsync(actor.Shop));
    }

    private async Task AssertRevocationAndRoleLossRefusalsAsync(ApiWebApplicationFactory host, HttpClient owner1, HttpClient owner2, Actor actor, JsonNode syncNode, string literalBundle)
    {
        await using (var db = Open(Migrator))
            await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET revoked_at = {_clock.Now} WHERE id = {actor.Session}", Ct);
        using (var revSync = await owner1.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = LegacyDevice, sales = new[] { syncNode } }, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, revSync.StatusCode);
        using (var revBundle = await PostRawRecoveryBundleAsync(owner1, "p0506-replay-op", literalBundle))
            Assert.Equal(HttpStatusCode.Unauthorized, revBundle.StatusCode);
        Assert.Equal("AlreadyPosted", (await SubmitRawRecoveryBundleAsync(owner2, "p0506-replay-op", literalBundle)).GetProperty("results")[0].GetProperty("status").GetString());

        await using (var db = Open(Migrator))
        {
            var m = await db.ShopMemberships.SingleAsync(x => x.ShopId == actor.Shop && x.UserId == actor.User, Ct);
            m.ChangeAuthority(ShopRole.Staff, MembershipStatus.Active);
            await db.SaveChangesAsync(Ct);
        }
        using (var staleOwner = await PostRawRecoveryBundleAsync(owner2, "p0506-replay-op", literalBundle))
            Assert.Contains(staleOwner.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        using var staffClient = await CreateSecondSessionClientAsync(host, actor, _clock.Now.AddDays(400));
        using (var staffBundle = await PostRawRecoveryBundleAsync(staffClient, "p0506-replay-op", literalBundle))
            Assert.Equal(HttpStatusCode.Forbidden, staffBundle.StatusCode);

        await using (var db = Open(Migrator))
        {
            var m = await db.ShopMemberships.SingleAsync(x => x.ShopId == actor.Shop && x.UserId == actor.User, Ct);
            m.ChangeAuthority(ShopRole.Owner, MembershipStatus.Suspended);
            await db.SaveChangesAsync(Ct);
        }
        using (var suspSync = await staffClient.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = LegacyDevice, sales = new[] { syncNode } }, Ct))
            Assert.Contains(suspSync.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
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
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(5), SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var result = await new EntitlementProjectionStore(db, _clock, new ShopLicenceFence(db, new LicenceActionPolicy(), _clock)).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
    }
}
