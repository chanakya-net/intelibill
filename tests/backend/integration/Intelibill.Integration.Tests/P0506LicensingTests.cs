using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0506LicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
    : OfflinePermitHttpFixture(fixture, output)
{
    private const string LegacyDevice = "legacy-counter-1";
    private const string LegacyVectorsFile = "p05-06-legacy-queue-vectors.json";

    [Fact]
    public async Task Later_authority_cannot_authorize_a_legacy_import()
    {
        var vectors = LoadLegacyVectors();
        AssertPinnedVectorHashes(vectors);

        var t = At("2027-01-20T12:00:00Z");
        var soldAt = t.AddMinutes(-10); // 2027-01-20T11:50:00Z — matches v1FrozenDocument vector
        using var host = CreateHost();
        await host.InitializeAsync();

        // 1. Primary case: sole applied snapshot has StartsAt = T-1d, AccessEndsAt = T+2h,
        //    but BOTH upstream verification timestamps are at T (> soldAt).
        using var laterOwner = CreateClient(host);
        var laterActor = await OpenHistoricalShopAsync(laterOwner, t, t.AddHours(2));
        var laterBatchId = await ConfigureRegularShopAndSeedVectorGoodsAsync(laterOwner, laterActor.Shop);
        await SeedUnanchoredLeaseAsync(laterActor, LegacyDevice, soldAt.AddMinutes(-50), t.AddDays(2), rangeStart: 1);
        var laterBefore = await ReadEconomicFactsAsync(laterActor.Shop);

        _clock.Now = t;
        var (laterRawBundle, laterInvoicesRaw, laterChecksum, laterExpectedHash) = BuildVectorRecoveryBundle(
            vectors, laterActor.Shop, LegacyDevice, laterActor.User, laterBatchId, soldAt, "A/2627/000002", t);

        var laterImported = await SubmitRawRecoveryBundleAsync(laterOwner, "p0506-later-only", laterRawBundle);
        var laterRow = Assert.Single(laterImported.GetProperty("results").EnumerateArray());
        Assert.Equal("ReviewOpened", laterRow.GetProperty("status").GetString());
        var laterCaseId = laterRow.GetProperty("caseId").GetGuid();

        var laterDetail = await GetCaseDetailAsync(laterOwner, laterCaseId);
        _output.WriteLine(
            "P0506 later-only: t={0:O} soldAt={1:O} issueType={2} reasonCodes={3} actions={4} checksum={5} hash={6}",
            t, soldAt,
            laterDetail.GetProperty("issueType").GetString(),
            laterDetail.GetProperty("reasonCodes"),
            laterDetail.GetProperty("availableActions"),
            laterChecksum, laterExpectedHash);

        Assert.Equal("LicenseHistoryReview", laterDetail.GetProperty("issueType").GetString());
        Assert.Contains("OfflineLicenceHistory.EvidenceMissing",
            laterDetail.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()));
        var laterHistory = laterDetail.GetProperty("licenceHistory");
        Assert.Equal("OfflineLicenceHistory.EvidenceMissing", laterHistory.GetProperty("currentReasonCode").GetString());
        Assert.Equal("HistoricalCoverageUnproven", laterHistory.GetProperty("currentSubreason").GetString());
        Assert.False(laterHistory.GetProperty("retryEligible").GetBoolean());
        Assert.Equal(["Escalate"],
            laterDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());

        Assert.Equal(laterBefore, await ReadEconomicFactsAsync(laterActor.Shop));
        await using (var db = Open(Migrator))
        {
            var receipt = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == laterActor.Shop, Ct);
            Assert.Null(receipt.LicencePermitId);
            Assert.Null(receipt.LicenceGrantId);
            Assert.Null(receipt.SaleId);
            Assert.Equal(t, receipt.ReceivedAt);
            Assert.Equal(laterExpectedHash, receipt.PayloadHash);
            Assert.Equal("A/2627/000002", receipt.InvoiceNumber);
            Assert.Equal(laterChecksum, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(laterInvoicesRaw))));
        }

        // 2. Boundary microsecond-after controls: even +1us on shop OR account upstream verification fails coverage.
        foreach (var (label, shopVerifiedAt, accountVerifiedAt) in new[]
        {
            ("shop+1us", soldAt.AddMicroseconds(1), soldAt),
            ("account+1us", soldAt, soldAt.AddMicroseconds(1)),
        })
        {
            using var boundaryOwner = CreateClient(host);
            var boundaryActor = await OpenHistoricalShopWithCustomVerificationAsync(
                boundaryOwner, applyAt: t, startsAt: t.AddDays(-1), endsAt: t.AddHours(2),
                shopVerifiedAt, accountVerifiedAt);
            var boundaryBatchId = await ConfigureRegularShopAndSeedVectorGoodsAsync(boundaryOwner, boundaryActor.Shop);
            await SeedUnanchoredLeaseAsync(boundaryActor, LegacyDevice, soldAt.AddMinutes(-50), t.AddDays(2), rangeStart: 1);

            _clock.Now = t;
            var (rawBundle, _, _, _) = BuildVectorRecoveryBundle(
                vectors, boundaryActor.Shop, LegacyDevice, boundaryActor.User, boundaryBatchId, soldAt, "A/2627/000002", t);
            var imported = await SubmitRawRecoveryBundleAsync(boundaryOwner, $"p0506-{label}", rawBundle);
            var caseId = imported.GetProperty("results")[0].GetProperty("caseId").GetGuid();
            var detail = await GetCaseDetailAsync(boundaryOwner, caseId);
            Assert.Equal("LicenseHistoryReview", detail.GetProperty("issueType").GetString());
            Assert.Equal("HistoricalCoverageUnproven", detail.GetProperty("licenceHistory").GetProperty("currentSubreason").GetString());
            Assert.False(detail.GetProperty("licenceHistory").GetProperty("retryEligible").GetBoolean());
            Assert.Equal(["Escalate"], detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
        }

        // 3. Control with timely verification (both upstream verification times == soldAt, applied later at T):
        //    proves the upstream verification clock — NOT local AppliedAt/IssuedAt — is the exact causal boundary.
        using var timelyOwner = CreateClient(host);
        var timelyActor = await OpenHistoricalShopWithCustomVerificationAsync(
            timelyOwner, applyAt: t, startsAt: t.AddDays(-1), endsAt: t.AddHours(2),
            shopVerifiedAt: soldAt, accountVerifiedAt: soldAt);
        var timelyBatchId = await ConfigureRegularShopAndSeedVectorGoodsAsync(timelyOwner, timelyActor.Shop);
        await SeedUnanchoredLeaseAsync(timelyActor, LegacyDevice, soldAt.AddMinutes(-50), t.AddDays(2), rangeStart: 1);

        _clock.Now = t;
        var (timelyRawBundle, _, _, _) = BuildVectorRecoveryBundle(
            vectors, timelyActor.Shop, LegacyDevice, timelyActor.User, timelyBatchId, soldAt, "A/2627/000002", t);
        var timelyImported = await SubmitRawRecoveryBundleAsync(timelyOwner, "p0506-timely-control", timelyRawBundle);
        var timelyCaseId = timelyImported.GetProperty("results")[0].GetProperty("caseId").GetGuid();
        var timelyDetail = await GetCaseDetailAsync(timelyOwner, timelyCaseId);
        Assert.Equal("RecoveryReview", timelyDetail.GetProperty("issueType").GetString());
        Assert.Contains("RetryHistoricalLease",
            timelyDetail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!));
    }

    private static JsonElement LoadLegacyVectors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, LegacyVectorsFile);
        Assert.True(File.Exists(path), $"Missing legacy vectors file at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        return doc.RootElement.Clone();
    }

    private static void AssertPinnedVectorHashes(JsonElement vectors)
    {
        var scope = vectors.GetProperty("scope");
        var shopId = scope.GetProperty("shopId").GetGuid();
        var deviceId = scope.GetProperty("deviceId").GetString()!;

        foreach (var name in new[] { "unversionedPreDocument", "v1FrozenDocument" })
        {
            var entry = vectors.GetProperty(name);
            var syncJson = entry.GetProperty("expectedSyncRequestJson").GetString()!;
            var cmd = JsonSerializer.Deserialize<OfflineSaleSyncCommand>(syncJson, JsonSerializerOptions.Web);
            Assert.NotNull(cmd);
            Assert.Equal(entry.GetProperty("expectedServerIdempotencyKey").GetString(),
                OfflineSaleSyncIdempotencyHasher.ComputeKey(deviceId, cmd!.ClientSaleId));
            Assert.Equal(entry.GetProperty("expectedServerPayloadHash").GetString(),
                OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, cmd));
        }

        var exported = vectors.GetProperty("previouslyExportedV1Bundle");
        var rawInvoices = exported.GetProperty("invoicesRawJson").GetString()!;
        Assert.Equal(exported.GetProperty("checksum").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawInvoices))));
    }

    private async Task<Actor> OpenHistoricalShopWithCustomVerificationAsync(
        HttpClient client,
        DateTimeOffset applyAt,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset shopVerifiedAt,
        DateTimeOffset accountVerifiedAt,
        bool enforce = true)
    {
        var actor = await OpenShopAsync(client, applyAt, endsAt, licensed: false);
        _clock.Now = applyAt;
        var binding = await BindAsync(actor.Shop);
        await ApplyCustomSnapshotAsync(binding, startsAt, endsAt, shopVerifiedAt, accountVerifiedAt, issuedAt: applyAt);
        await using (var db = Open(Migrator))
            await LegacyOfflineExpiryHistory.SeedAsync(db, actor.Shop);
        if (enforce)
            await EnforceAsync(actor.Shop);
        return actor with { Binding = binding };
    }

    private async Task ApplyCustomSnapshotAsync(
        Binding binding,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset shopVerifiedAt,
        DateTimeOffset accountVerifiedAt,
        DateTimeOffset issuedAt,
        long revision = 1,
        string restriction = "Unrestricted",
        string state = "Active",
        DateTimeOffset? suspensionEffectiveAt = null,
        long? accountRevision = null)
    {
        await using var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var suspension = suspensionEffectiveAt is { } effectiveAt
            ? new CustomerSuspensionV1(restriction, effectiveAt, "Fixture suspension")
            : null;
        var snapshot = new EntitlementSnapshotV1(
            1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request,
            new(accountRevision ?? revision, accountVerifiedAt, restriction, null, suspension),
            new(revision, shopVerifiedAt, state, binding.Term, "Paid", startsAt, endsAt, endsAt, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(
            Guid.NewGuid(), "test-key", "test-public-key", issuedAt, _clock.Now.AddMinutes(5),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        var ack = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json);
        Assert.True(ack?.Outcome == "Applied", ack?.Disposition);
    }

    private static async Task<Guid> ConfigureRegularShopAndSeedVectorGoodsAsync(HttpClient client, Guid shopId)
    {
        using var taxResp = await client.PutAsJsonAsync($"/api/shops/{shopId:D}/tax-profile", new
        {
            taxRegime = "Regular",
            stateCode = "29",
            gstNumber = "29AAPFU0939F1ZR",
            legalName = "Frozen Legacy Traders Pvt Ltd",
            eInvoiceStatus = "BelowThreshold",
            invoiceSeriesCode = "A",
        }, Ct);
        await ReadSuccessAsync(taxResp);

        using var inboundResp = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "Steel Bottle 1L",
            barcode = "LEG-671D",
            uom = "pcs",
            hsnCode = "7323",
            batchNumber = "LOT-V1",
            quantity = 10m,
            totalPurchaseCost = 700m,
            mrp = 120m,
            salesPrice = 100m,
            taxRatePercent = 18m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
        }, Ct);
        var inbound = await ReadSuccessAsync(inboundResp);
        if (inbound.TryGetProperty("inventoryBatchId", out var invBatch))
            return invBatch.GetGuid();
        if (inbound.TryGetProperty("batchId", out var batchProp))
            return batchProp.GetGuid();
        return inbound.GetProperty("id").GetGuid();
    }

    private async Task<InvoiceLease> SeedUnanchoredLeaseAsync(
        Actor actor,
        string deviceId,
        DateTimeOffset reservedAt,
        DateTimeOffset expiresAt,
        int rangeStart,
        Guid? reservedByUserId = null,
        Guid? reservedBySessionId = null)
    {
        await using var db = Open(Migrator);
        var year = FiscalYear.ForDate(reservedAt);
        var sequence = await db.InvoiceSequences.FirstOrDefaultAsync(
            s => s.ShopId == actor.Shop && s.FiscalYearStart == year.StartYear && s.SeriesCode == "A", Ct);
        if (sequence is null)
        {
            sequence = InvoiceSequence.Create(actor.Shop, year);
            db.InvoiceSequences.Add(sequence);
        }
        var lease = InvoiceLease.Create(
            actor.Shop, sequence.Id, deviceId, year.StartYear, "A/2627/", rangeStart, rangeStart + 24, 6,
            reservedAt, expiresAt, reservedByUserId ?? actor.User, reservedBySessionId ?? actor.Session);
        db.InvoiceLeases.Add(lease);
        await db.SaveChangesAsync(Ct);
        return lease;
    }

    private static (string BundleText, string InvoicesRaw, string Checksum, string ExpectedHash) BuildVectorRecoveryBundle(
        JsonElement vectors,
        Guid shopId,
        string deviceId,
        Guid? issuedByUserId,
        Guid batchId,
        DateTimeOffset soldAt,
        string invoiceNumber,
        DateTimeOffset exportedAt,
        string vectorName = "v1FrozenDocument",
        bool prettyWhitespace = true,
        string? clientSaleIdOverride = null)
    {
        var templateJson = vectors.GetProperty(vectorName).GetProperty("expectedSyncRequestJson").GetString()!;
        var node = JsonNode.Parse(templateJson)!.AsObject();
        node["invoiceNumber"] = invoiceNumber;
        node["soldAt"] = soldAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        if (clientSaleIdOverride is not null)
            node["clientSaleId"] = clientSaleIdOverride;
        if (issuedByUserId is { } issuer)
            node["issuedByUserId"] = issuer.ToString("D");
        else
            node.Remove("issuedByUserId");
        node["items"]![0]!["inventoryBatchId"] = batchId.ToString("D");

        var invoiceJson = prettyWhitespace
            ? node.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            : node.ToJsonString();
        var invoicesRaw = prettyWhitespace
            ? "[\n  " + invoiceJson.Replace("\n", "\n  ") + "\n]"
            : "[" + invoiceJson + "]";
        var checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(invoicesRaw)));
        var bundleText =
            "{\"format\":\"intelibill.offline-recovery\",\"version\":1,\"shopId\":" + JsonSerializer.Serialize(shopId) +
            ",\"deviceId\":" + JsonSerializer.Serialize(deviceId) +
            ",\"exportedAt\":" + JsonSerializer.Serialize(exportedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)) +
            ",\"checksum\":" + JsonSerializer.Serialize(checksum) +
            ",\"invoices\":" + invoicesRaw + "}";

        var parsed = JsonSerializer.Deserialize<OfflineSaleSyncCommand>(node.ToJsonString(), JsonSerializerOptions.Web)!;
        var expectedHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(shopId, deviceId, parsed);
        return (bundleText, invoicesRaw, checksum, expectedHash);
    }

    private static async Task<JsonElement> SubmitRawRecoveryBundleAsync(
        HttpClient client,
        string operationId,
        string rawBundleText)
    {
        var body = "{\"operationId\":" + JsonSerializer.Serialize(operationId) + ",\"bundle\":" + rawBundleText + "}";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/offline-reconciliation/recovery-submissions", content, Ct);
        return await ReadSuccessAsync(response);
    }
}
