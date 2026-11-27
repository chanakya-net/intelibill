using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    [InlineData("Service", true)]
    [InlineData("Goods", true)]
    [InlineData("Service", false)]
    public async Task Incompatible_retained_line_service_semantics_cannot_be_approved_into_a_sale(string mutatedLineType, bool setServiceId)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var (actor, caseId, version, savedHash, serviceId) = await OpenLegacyGoodsRecoveryCaseAsync(host, owner);

        var serviceJson = setServiceId ? $"\"{serviceId:D}\"" : "null";
        await using (var db = Open(Migrator))
            Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(
                "UPDATE offline_submissions SET payload_json = jsonb_set(jsonb_set(payload_json, '{{items,0,lineType}}', {1}::jsonb), '{{items,0,serviceId}}', {2}::jsonb) WHERE shop_id = {0}",
                [actor.Shop, JsonSerializer.Serialize(mutatedLineType), serviceJson], Ct));
        var before = await ReadEconomicFactsAsync(actor.Shop);

        await AssertProblemAsync(
            await owner.PostAsJsonAsync($"/api/offline-reconciliation/{caseId:D}/resolve", new
            {
                operationId = $"p0506-line-sem-{mutatedLineType}-{setServiceId}", expectedVersion = version,
                action = "RetryHistoricalLease", reason = "Approve envelope with mutated omitted line fields",
            }, Ct),
            HttpStatusCode.Conflict, "Reconciliation.ReplayRejected");

        Assert.Equal(before, await ReadEconomicFactsAsync(actor.Shop));
        Assert.Equal("Open", (await GetCaseDetailAsync(owner, caseId)).GetProperty("status").GetString());
        await using var check = Open(Migrator);
        Assert.False(await check.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
        var receipt = await check.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop, Ct);
        Assert.Equal((savedHash, (Guid?)null), (receipt.PayloadHash, receipt.SaleId));
    }

    [Fact]
    public async Task Retained_legacy_goods_envelope_with_omitted_service_fields_approves_as_goods()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var (actor, caseId, version, savedHash, _) = await OpenLegacyGoodsRecoveryCaseAsync(host, owner);

        await using (var db = Open(Migrator))
            Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(
                "UPDATE offline_submissions SET payload_json = (payload_json #- '{{items,0,lineType}}') #- '{{items,0,serviceId}}' WHERE shop_id = {0}",
                [actor.Shop], Ct));
        var before = await ReadEconomicFactsAsync(actor.Shop);

        var resolved = await ResolveCaseAsync(owner, caseId, version,
            "p0506-omitted-fields-approve", "RetryHistoricalLease", "Historical envelope omitted service fields");
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());

        var after = await ReadEconomicFactsAsync(actor.Shop);
        Assert.Equal((before.Sales + 1, before.OutStockTransactions + 1, before.BatchQuantity - 2m),
            (after.Sales, after.OutStockTransactions, after.BatchQuantity));
        await using var check = Open(Migrator);
        var sale = await check.Sales.Include(s => s.Items).SingleAsync(s => s.ShopId == actor.Shop, Ct);
        var line = Assert.Single(sale.Items);
        Assert.Equal((savedHash, SaleLineType.Goods, (Guid?)null), (sale.RequestHash, line.LineType, line.ServiceId));
    }

    [Fact]
    public async Task Direct_handler_service_and_mixed_commands_preserve_outcomes_with_materialized_receipts()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        var (actor, serviceId, batchId) = await SeedDirectSyncShopAsync(host, owner);

        _clock.Now = At("2027-01-20T12:00:00Z");
        var before = await ReadEconomicFactsAsync(actor.Shop);
        var sales = new[]
        {
            BuildDirectCommand("direct-svc-only", "INV-2026-27-000001", actor.User, [DirectServiceLine(serviceId)]),
            BuildDirectCommand("direct-mixed", "INV-2026-27-000002", actor.User, [DirectGoodsLine(batchId), DirectServiceLine(serviceId)]),
            BuildDirectCommand("direct-missing-svc", "INV-2026-27-000003", actor.User, [DirectServiceLine(null)]),
        };

        using var _ = AmbientSessionContext.Enter(actor.User, actor.Shop);
        var results = await InvokeDirectSyncAsync(host, actor, sales);
        Assert.Equal(["created", "created", "failed"], results.Select(r => r.Status).ToArray());
        Assert.Contains(results[2].Errors, e => e.Code == "Sale.ServiceIdRequired");
        var originalSaleIds = new[] { Assert.NotNull(results[0].SaleId), Assert.NotNull(results[1].SaleId) };

        var after = await ReadEconomicFactsAsync(actor.Shop);
        Assert.Equal((before.Sales + 2, before.OutStockTransactions + 1, before.BatchQuantity - 2m),
            (after.Sales, after.OutStockTransactions, after.BatchQuantity));

        var redelivered = await InvokeDirectSyncAsync(host, actor, [sales[0], sales[1]]);
        Assert.Equal(["duplicate", "duplicate"], redelivered.Select(r => r.Status).ToArray());
        Assert.Equal(originalSaleIds, redelivered.Select(r => Assert.NotNull(r.SaleId)).ToArray());
        Assert.All(redelivered, r => { Assert.Null(r.ReviewCaseId); Assert.Empty(r.Errors); });
        Assert.Equal(after, await ReadEconomicFactsAsync(actor.Shop));
        await using var db = Open(Migrator);
        Assert.Equal(0, await db.ReconciliationIssues.CountAsync(x => x.ShopId == actor.Shop, Ct));
        Assert.Equal(3, await db.OfflineSubmissions.CountAsync(x => x.ShopId == actor.Shop, Ct));
    }

    private async Task<(Actor Actor, Guid ServiceId, Guid BatchId)> SeedDirectSyncShopAsync(
        ApiWebApplicationFactory host, HttpClient owner)
    {
        var vectors = LoadLegacyVectors();
        var verifiedAt = At("2027-01-20T11:20:00Z");
        var actor = await OpenPinnedVectorShopAsync(
            host, owner, vectors, verifiedAt, verifiedAt.AddDays(-1), At("2027-01-20T12:20:00Z"),
            verifiedAt, verifiedAt, include671dLease: false, include7ff1Lease: false);
        await SeedUnanchoredLeaseAsync(actor, LegacyDevice, At("2027-01-20T11:00:00Z"), At("2027-01-20T13:00:00Z"),
            1, "INV-2026-27-", withReserver: true);

        await using var db = Open(Migrator);
        var service = Service.Create(actor.Shop, "SVC-001", "Repair Service", null, 500m, "9987", 0m, false, true, actor.User);
        db.Services.Add(service);
        await db.SaveChangesAsync(Ct);
        var batchId = vectors.GetProperty("unversionedPreDocument").GetProperty("inventoryBatchId").GetGuid();
        return (actor, service.Id, batchId);
    }

    private static async Task<IReadOnlyList<OfflineSaleSyncResultDto>> InvokeDirectSyncAsync(
        ApiWebApplicationFactory host, Actor actor, IReadOnlyList<OfflineSaleSyncCommand> sales)
    {
        using var scope = host.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SyncOfflineSalesCommandHandler>();
        var response = await handler.HandleAsync(new SyncOfflineSalesCommand(actor.User, actor.Shop, LegacyDevice, sales), Ct);
        Assert.False(response.IsError);
        return response.Value.Results;
    }

    private static OfflineSaleSyncCommand BuildDirectCommand(
        string clientSaleId, string invoiceNumber, Guid issuerId, IReadOnlyList<OfflineSaleSyncLineCommand> items)
    {
        var total = items.Sum(x => x.TotalAmount);
        return new(clientSaleId, invoiceNumber, At("2027-01-20T11:50:00Z"), null, "Walk-in", "+919876543210",
            PaymentMethod.Cash, total, 0m, total, total, 0m, 0m, total,
            InstantDiscountType.None, 0m, null, null, null, null, items, CalculationVersion: 1, IssuedByUserId: issuerId);
    }

    private static OfflineSaleSyncLineCommand DirectGoodsLine(Guid batchId) =>
        new("LEG-7FF1", "LOT-OLD", "Legacy Rice 1kg", 2m, 70m, 100m, 120m, 0m, false, batchId,
            200m, 0m, 0m, 200m, 0m, 200m, null, null, InstantDiscountType.None, 0m, "1006");

    private static OfflineSaleSyncLineCommand DirectServiceLine(Guid? serviceId) =>
        new("SVC-001", "", "Repair Service", 1m, 0m, 500m, 0m, 0m, false, Guid.Empty,
            500m, 0m, 0m, 500m, 0m, 500m, null, null, InstantDiscountType.None, 0m, "9987",
            LineType: "Service", ServiceId: serviceId);

    private async Task<(Actor Actor, Guid CaseId, uint Version, string SavedHash, Guid ServiceId)> OpenLegacyGoodsRecoveryCaseAsync(
        ApiWebApplicationFactory host, HttpClient owner)
    {
        var vectors = LoadLegacyVectors();
        var verifiedAt = At("2027-01-20T11:20:00Z");
        var actor = await OpenPinnedVectorShopAsync(
            host, owner, vectors, verifiedAt, verifiedAt.AddDays(-1), At("2027-01-20T12:20:00Z"),
            verifiedAt, verifiedAt, include671dLease: false, include7ff1Lease: false);
        await SeedUnanchoredLeaseAsync(actor, LegacyDevice, At("2027-01-20T11:00:00Z"), At("2027-01-23T11:00:00Z"),
            1, "INV-2026-27-", withReserver: true);

        Guid serviceId;
        await using (var db = Open(Migrator))
        {
            var service = Service.Create(actor.Shop, "LEG-7FF1", "Legacy Rice 1kg", null, 100m, "1006", 0m, false, true, actor.User);
            db.Services.Add(service);
            await db.SaveChangesAsync(Ct);
            serviceId = service.Id;
        }

        _clock.Now = At("2027-01-20T13:00:00Z");
        var legacyNode = JsonNode.Parse(vectors.GetProperty("unversionedPreDocument").GetProperty("expectedSyncRequestJson").GetString()!)!.AsObject();
        legacyNode["issuedByUserId"] = actor.User.ToString("D");
        legacyNode["calculationVersion"] = 1;
        var imported = await SubmitRawRecoveryBundleAsync(owner, "p0506-legacy-goods-import",
            BuildCompactBundle(actor.Shop, LegacyDevice, _clock.Now, legacyNode));
        var caseId = Assert.Single(imported.GetProperty("results").EnumerateArray()).GetProperty("caseId").GetGuid();
        var opened = await GetCaseDetailAsync(owner, caseId);
        Assert.Equal("RecoveryReview", opened.GetProperty("issueType").GetString());
        Assert.Contains("RetryHistoricalLease", opened.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()!));

        await using var check = Open(Migrator);
        var savedHash = (await check.OfflineSubmissions.AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop, Ct)).PayloadHash;
        return (actor, caseId, opened.GetProperty("version").GetUInt32(), savedHash, serviceId);
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
