using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0506LicensingTests
{
    private const string LiveLegalName = "Live Changed Traders LLP";
    private const string LiveGstin = "27AAPFU0939F1ZV";

    [Fact]
    public async Task Tampered_retained_envelope_cannot_be_approved_into_a_sale()
    {
        var vectors = LoadLegacyVectors();
        var v6 = vectors.GetProperty("v1FrozenDocument");
        var pinnedHash = v6.GetProperty("expectedServerPayloadHash").GetString()!;
        var verifiedAt = At("2027-01-20T11:20:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenPinnedVectorShopAsync(
            host, owner, vectors, verifiedAt, verifiedAt.AddDays(-1), At("2027-01-20T12:20:00Z"), verifiedAt, verifiedAt);

        _clock.Now = At("2027-01-20T13:00:00Z");
        var imported = await SubmitRawRecoveryBundleAsync(owner, "p0506-tamper-import", v6.GetProperty("exportedBundleText").GetString()!);
        var caseId = imported.GetProperty("results")[0].GetProperty("caseId").GetGuid();
        var opened = await GetCaseDetailAsync(owner, caseId);
        Assert.Equal("RecoveryReview", opened.GetProperty("issueType").GetString());
        Assert.Contains("RetryHistoricalLease", opened.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!));

        // The stored envelope stays parseable but no longer agrees with the hash saved at first receipt.
        await using (var db = Open(Migrator))
            Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(
                "UPDATE offline_submissions SET payload_json = jsonb_set(payload_json, '{{customerName}}', '\"Tampered Legacy Customer\"') WHERE shop_id = {0}",
                [actor.Shop], Ct));
        var before = await ReadEconomicFactsAsync(actor.Shop);

        await AssertProblemAsync(
            await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
            {
                operationId = "p0506-tamper-approve", expectedVersion = opened.GetProperty("version").GetUInt32(),
                action = "RetryHistoricalLease", reason = "Approve a changed envelope",
            }, Ct),
            HttpStatusCode.Conflict, "Reconciliation.ReplayRejected");

        Assert.Equal(before, await ReadEconomicFactsAsync(actor.Shop));
        Assert.NotEqual("Resolved", (await GetCaseDetailAsync(owner, caseId)).GetProperty("status").GetString());
        await using var check = Open(Migrator);
        Assert.False(await check.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
        var receipt = await check.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop, Ct);
        Assert.Equal((pinnedHash, (Guid?)null), (receipt.PayloadHash, receipt.SaleId));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    public async Task Open_expiry_review_with_malformed_retained_items_stays_readable_and_escalates(string retainedJson)
    {
        var vectors = LoadLegacyVectors();
        var verifiedAt = At("2027-01-20T11:20:00Z");

        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var actor = await OpenPinnedVectorShopAsync(
            host, owner, vectors, verifiedAt, verifiedAt.AddDays(-1), At("2027-01-20T12:20:00Z"), verifiedAt, verifiedAt);
        _clock.Now = At("2027-01-20T12:05:00Z");
        var caseId = await SeedOpenExpiryReviewAsync(actor, retainedJson);
        var before = await ReadEconomicFactsAsync(actor.Shop);

        var detail = await GetCaseDetailAsync(owner, caseId);
        Assert.Equal("ExpiryReview", detail.GetProperty("issueType").GetString());
        Assert.Equal(["Escalate"], detail.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!).ToArray());

        var escalated = await ResolveCaseAsync(owner, caseId, detail.GetProperty("version").GetUInt32(),
            "p0506-expiry-escalate", "Escalate", "Retained items cannot be read");
        Assert.Equal("Escalated", escalated.GetProperty("status").GetString());
        Assert.Equal(before, await ReadEconomicFactsAsync(actor.Shop));
    }

    private async Task<Guid> SeedOpenExpiryReviewAsync(Actor actor, string retainedJson)
    {
        await using var db = Open(Migrator);
        var receipt = OfflineSubmission.Record(
            actor.Shop, LegacyDevice, "legacy-expiry-damaged", "A/2627/000009", new string('A', 64), retainedJson,
            At("2027-01-20T11:50:00Z"), At("2027-01-20T12:00:00Z"), OfflineSubmissionSource.Sync,
            actor.User, actor.User, actor.User, null, null);
        db.OfflineSubmissions.Add(receipt);
        var issue = ReconciliationIssue.Create(
            actor.Shop, null, receipt.ClientSaleId, receipt.DeviceId, ReconciliationIssueType.ExpiryReview,
            "Expiry.HistoryUnknown", "Retained expiry evidence could not be verified", actor.User,
            invoiceNumber: receipt.InvoiceNumber, offlineSubmissionId: receipt.Id);
        db.ReconciliationIssues.Add(issue);
        await db.SaveChangesAsync(Ct);
        return issue.Id;
    }

    /// <summary>The seller identity and series frozen in the archived 671d51df3 document.</summary>
    private static InvoiceSellerProfile ArchivedSellerProfile()
    {
        var document = ArchivedV1Document();
        var seller = document.GetProperty("seller");
        return new InvoiceSellerProfile(
            new InvoiceSeller(
                seller.GetProperty("legalName").GetString()!, seller.GetProperty("tradeName").GetString()!,
                seller.GetProperty("address").GetString()!, seller.GetProperty("stateCode").GetString()!,
                seller.GetProperty("stateName").GetString()!, seller.GetProperty("gstin").GetString(),
                Enum.Parse<TaxRegime>(seller.GetProperty("taxRegime").GetString()!)),
            EInvoiceStatus.BelowThreshold,
            document.GetProperty("seriesCode").GetString()!);
    }

    private static JsonElement ArchivedV1Document()
    {
        var queueRecord = LoadLegacyVectors().GetProperty("v1FrozenDocument").GetProperty("queueRecordJson").GetString()!;
        using var parsed = JsonDocument.Parse(queueRecord);
        return parsed.RootElement.GetProperty("payload").GetProperty("document").Clone();
    }

    private async Task ChangeLiveSellerDetailsAsync(Guid shopId)
    {
        await using var db = Open(Migrator);
        var shop = await db.Shops.SingleAsync(x => x.Id == shopId, Ct);
        var owner = await db.ShopMemberships.Where(x => x.ShopId == shopId).Select(x => x.UserId).FirstAsync(Ct);
        Assert.False(shop.DeclareTaxProfile(LiveLegalName, "27", TaxRegime.Regular, LiveGstin, "B",
            EInvoiceStatus.BelowThreshold, owner, _clock.Now).IsError);
        Assert.False(shop.UpdateDetails("Live Changed Shop Name", "99 New Street", "Mumbai", "Maharashtra", "400001", null, null, null).IsError);
        await db.SaveChangesAsync(Ct);
    }

    private static void AssertArchivedSellerAndNumbering(IssuedInvoiceSnapshot posted)
    {
        var archived = ArchivedV1Document();
        var seller = archived.GetProperty("seller");
        Assert.Equal(
            (archived.GetProperty("number").GetString(), archived.GetProperty("seriesCode").GetString(), archived.GetProperty("fiscalYearStart").GetInt32()),
            (posted.Number, posted.SeriesCode, posted.FiscalYearStart));
        Assert.Equal(
            (seller.GetProperty("legalName").GetString(), seller.GetProperty("tradeName").GetString(), seller.GetProperty("gstin").GetString(),
                seller.GetProperty("address").GetString(), seller.GetProperty("stateCode").GetString()),
            (posted.Seller.LegalName, posted.Seller.TradeName, posted.Seller.Gstin, posted.Seller.Address, posted.Seller.StateCode));
        Assert.NotEqual(LiveLegalName, posted.Seller.LegalName);
        Assert.NotEqual(LiveGstin, posted.Seller.Gstin);
        Assert.DoesNotContain("99 New Street", posted.Seller.Address, StringComparison.Ordinal);
    }
}
