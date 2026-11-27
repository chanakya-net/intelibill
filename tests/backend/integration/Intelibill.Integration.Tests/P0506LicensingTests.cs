using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0506LicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
    : OfflinePermitHttpFixture(fixture, output)
{
    [Fact]
    public async Task Later_authority_cannot_authorize_a_legacy_import()
    {
        var vectors = LoadLegacyVectors();
        AssertPinnedVectorHashes(vectors);

        var t = At("2027-01-20T12:00:00Z");
        var soldAt = At("2027-01-20T11:50:00Z");
        var v6 = vectors.GetProperty("v1FrozenDocument");
        var literalBundle = v6.GetProperty("exportedBundleText").GetString()!;
        var pinnedHash = v6.GetProperty("expectedServerPayloadHash").GetString()!;

        using var host = CreateHost();
        await host.InitializeAsync();
        using var laterOwner = CreateClient(host);
        var laterActor = await OpenPinnedVectorShopAsync(
            host, laterOwner, vectors, applyAt: t, startsAt: t.AddDays(-1), endsAt: t.AddHours(2),
            shopVerifiedAt: t, accountVerifiedAt: t);
        var before = await ReadEconomicFactsAsync(laterActor.Shop);

        _clock.Now = t;
        var imported = await SubmitRawRecoveryBundleAsync(laterOwner, "p0506-later-only", literalBundle);
        var row = Assert.Single(imported.GetProperty("results").EnumerateArray());
        Assert.Equal("ReviewOpened", row.GetProperty("status").GetString());
        var detail = await GetCaseDetailAsync(laterOwner, row.GetProperty("caseId").GetGuid());

        AssertHistoricalCoverageUnproven(detail);
        Assert.Equal(before, await ReadEconomicFactsAsync(laterActor.Shop));
        await AssertRetainedUnpostedReceiptAsync(laterActor.Shop, t, pinnedHash, "A/2627/000002");

        await AssertBoundaryCaseAsync(host, vectors, t, soldAt.AddMicroseconds(1), soldAt, "shop+1us", expectTimely: false);
        await AssertBoundaryCaseAsync(host, vectors, t, soldAt, soldAt.AddMicroseconds(1), "account+1us", expectTimely: false);
        await AssertBoundaryCaseAsync(host, vectors, t, soldAt, soldAt, "timely-control", expectTimely: true);
    }

    private static void AssertHistoricalCoverageUnproven(JsonElement detail)
    {
        Assert.Equal("LicenseHistoryReview", detail.GetProperty("issueType").GetString());
        Assert.Contains("OfflineLicenceHistory.EvidenceMissing",
            detail.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()));
        var history = detail.GetProperty("licenceHistory");
        Assert.Equal("OfflineLicenceHistory.EvidenceMissing", history.GetProperty("currentReasonCode").GetString());
        Assert.Equal("HistoricalCoverageUnproven", history.GetProperty("currentSubreason").GetString());
        Assert.False(history.GetProperty("retryEligible").GetBoolean());
        Assert.Equal(["Escalate"], detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());
    }

    private async Task AssertRetainedUnpostedReceiptAsync(Guid shopId, DateTimeOffset receivedAt, string expectedHash, string invoiceNumber)
    {
        await using var db = Open(Migrator);
        var receipt = await db.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == shopId, Ct);
        Assert.Null(receipt.LicencePermitId);
        Assert.Null(receipt.LicenceGrantId);
        Assert.Null(receipt.SaleId);
        Assert.Equal(receivedAt, receipt.ReceivedAt);
        Assert.Equal(expectedHash, receipt.PayloadHash);
        Assert.Equal(invoiceNumber, receipt.InvoiceNumber);
    }

    private async Task AssertBoundaryCaseAsync(
        ApiWebApplicationFactory host,
        JsonElement vectors,
        DateTimeOffset applyAt,
        DateTimeOffset shopVerifiedAt,
        DateTimeOffset accountVerifiedAt,
        string label,
        bool expectTimely)
    {
        using var owner = CreateClient(host);
        var actor = await OpenHistoricalShopWithCustomVerificationAsync(
            owner, applyAt, applyAt.AddDays(-1), applyAt.AddHours(2), shopVerifiedAt, accountVerifiedAt);
        await SeedUnanchoredLeaseAsync(actor, LegacyDevice, At("2027-01-20T11:00:00Z"), applyAt.AddDays(2), 1);

        Guid batchId;
        await using (var db = Open(Migrator))
        {
            batchId = (await db.InventoryBatches.SingleAsync(b => b.ShopId == actor.Shop, Ct)).Id;
        }
        var invoice = JsonNode.Parse(vectors.GetProperty("unversionedPreDocument").GetProperty("expectedSyncRequestJson").GetString()!)!.AsObject();
        invoice["invoiceNumber"] = "A/2627/000002";
        invoice["soldAt"] = "2027-01-20T11:50:00.000Z";
        invoice["issuedByUserId"] = actor.User.ToString("D");
        invoice["calculationVersion"] = 1;
        invoice["items"]![0]!["inventoryBatchId"] = batchId.ToString("D");

        _clock.Now = applyAt;
        var rawBundle = BuildCompactBundle(actor.Shop, LegacyDevice, applyAt, invoice);
        var imported = await SubmitRawRecoveryBundleAsync(owner, $"p0506-{label}", rawBundle);
        var detail = await GetCaseDetailAsync(owner, imported.GetProperty("results")[0].GetProperty("caseId").GetGuid());
        if (expectTimely)
        {
            Assert.Equal("RecoveryReview", detail.GetProperty("issueType").GetString());
            Assert.Contains("RetryHistoricalLease", detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!));
        }
        else
        {
            AssertHistoricalCoverageUnproven(detail);
        }
    }
}
