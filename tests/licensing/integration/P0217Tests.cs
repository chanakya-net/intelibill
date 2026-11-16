using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Policies;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0213Tests;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed class P0217Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task ExistingShopTradesBeforeTrialAndWhileProjectionIsPendingOrApplied()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var staff = await SignInAsync(fixture.Hosts, ShopRole.Staff, owner.ShopId);
        var stock = await IntakeAsync(owner);
        var saleKey = Guid.NewGuid();
        var firstSale = await SellAsync(owner, stock, saleKey);
        await AssertStockAsync(owner, stock, 1);

        var account = await LinkAsync(fixture, owner);
        await ConfirmAsync(fixture);
        var affiliation = await fixture.LatestSnapshotAsync(owner.ShopId);
        var operation = fixture.Trial.Accept();
        var trial = await StartTrialAsync(owner, operation);
        var facts = await TrialFactsAsync(fixture, owner.ShopId);
        Assert.Equal(trial, await P0210Tests.StartAndWaitAsync(owner, operation));

        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        await AssertSynchronizationAsync(fixture, owner, snapshot, "Pending", "Queued", affiliation);
        var (oldHead, _) = await HeadsAsync(owner.ShopId, account);
        Assert.Equal(affiliation.Shop!.Revision, oldHead.Revision);
        await SellAsync(owner, stock, Guid.NewGuid());

        await fixture.DrainAsync(source: true);
        await AssertSynchronizationAsync(fixture, owner, snapshot, "Pending", "AwaitingAcknowledgement", affiliation);
        await AssertHeadsAsync(snapshot);
        Assert.Equal(2L, await EvidenceCountAsync(fixture, owner.ShopId));
        await SellAsync(owner, stock, Guid.NewGuid());

        await fixture.DrainAsync(source: false);
        await AssertSynchronizationAsync(fixture, owner, snapshot, "Applied", "Acknowledged", snapshot);
        await SellAsync(owner, stock, Guid.NewGuid());
        Assert.Equal(firstSale, await SellAsync(owner, stock, saleKey));
        await AssertStockAsync(owner, stock, 4);
        await AssertStaffDeniedAsync(staff);
        Assert.Equal(facts, await TrialFactsAsync(fixture, owner.ShopId));
    }

    [Fact]
    public async Task CrashedAndReorderedDeliveryCannotRefreshStaleEvidenceOrBlockObserveOnlySales()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        await LinkAsync(fixture, owner);
        var older = await fixture.LatestSnapshotAsync(owner.ShopId);
        // The pump orders jobs by due time and then event ID. Put the older event first.
        fixture.Hosts.Fixture.Clock.Now += TimeSpan.FromSeconds(1);
        var trial = await StartTrialAsync(owner, fixture.Trial.Accept());
        var facts = await TrialFactsAsync(fixture, owner.ShopId);
        var active = await fixture.LatestSnapshotAsync(owner.ShopId);
        var activeReceipt = await SendReceiptAsync(fixture, active);
        Assert.Equal("Applied", activeReceipt.Disposition);
        var activeTimes = await EvidenceTimesAsync(fixture, active.EventId);

        var stock = await IntakeAsync(owner);
        await RecoverOlderDeliveryAsync(fixture, owner, older, active);
        await AssertSynchronizationAsync(fixture, owner, active, "Pending", "AwaitingAcknowledgement", null);
        await SellAsync(owner, stock, Guid.NewGuid());
        await fixture.DrainAsync(source: false);
        await AssertSynchronizationAsync(fixture, owner, active, "Applied", "Acknowledged", active);
        await AssertConfirmedAsync(fixture, older.EventId);
        await AssertConfirmedAsync(fixture, active.EventId);
        Assert.Equal(activeReceipt, await SendReceiptAsync(fixture, active));
        Assert.Equal(activeTimes, await EvidenceTimesAsync(fixture, active.EventId));
        Assert.Equal(2L, await EvidenceCountAsync(fixture, owner.ShopId));

        // Keep the unrelated account outside the due verification scan for this trial.
        fixture.Hosts.StatusClock.Now = P0210Fixture.Start.AddHours(2);
        var witness = await CreateTradingShopAsync(fixture.Hosts);
        var unrelatedAccount = await CreateAccountAsync(witness.Client);
        await P0213Fixture.LinkAsync(unrelatedAccount, witness, witness);
        var unrelated = await fixture.LatestSnapshotAsync(witness.ShopId);
        await ConfirmAsync(fixture);
        var witnessStock = await IntakeAsync(witness);
        await SellAsync(witness, witnessStock, Guid.NewGuid());

        await AssertStaleThenRefreshAsync(fixture, owner, stock, active, trial.EndsAt);
        Assert.Equal(facts, await TrialFactsAsync(fixture, owner.ShopId));
        await AssertStockAsync(witness, witnessStock, 1);
        Assert.Equal(unrelated, await fixture.LatestSnapshotAsync(witness.ShopId));
        await AssertHeadsAsync(unrelated);
    }

    [Fact]
    public async Task ExactTrialEndCannotBeRevivedByAnOlderSnapshot()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        await LinkAsync(fixture, owner);
        var operation = fixture.Trial.Accept();
        var trial = await StartTrialAsync(owner, operation);
        var facts = await TrialFactsAsync(fixture, owner.ShopId);
        var active = await fixture.LatestSnapshotAsync(owner.ShopId);
        var original = await SendReceiptAsync(fixture, active);
        await ConfirmAsync(fixture);

        fixture.Hosts.StatusClock.Now = trial.EndsAt.AddMicroseconds(-1);
        await AssertTrialStatusAsync(owner, trial.EndsAt, "Active");
        fixture.Hosts.StatusClock.Now = trial.EndsAt;
        await AssertTrialStatusAsync(owner, trial.EndsAt, "Expired");
        await fixture.ScanAsync(source: true);
        await ConfirmAsync(fixture);
        var expired = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.True(expired.Shop!.Revision > active.Shop!.Revision);
        Assert.Equal("Expired", expired.Shop.State);
        await AssertSynchronizationAsync(fixture, owner, expired, "Applied", "Acknowledged", expired);

        fixture.Hosts.StatusClock.Now = trial.EndsAt.AddMicroseconds(1);
        Assert.Equal(original, await SendReceiptAsync(fixture, active));
        var (head, restriction) = await AssertHeadsAsync(expired);
        Assert.Equal(LicenseProjectionDecision.Expired, Evaluate(head, restriction, fixture.Hosts.StatusClock.Now));
        await AssertTrialStatusAsync(owner, trial.EndsAt, "Expired");
        Assert.Equal(trial, await P0210Tests.StartAndWaitAsync(owner, operation));
        Assert.Equal("AlreadyUsed", (await P0210Tests.StartAndWaitAsync(owner, fixture.Trial.Accept())).Reason);
        Assert.Equal(facts, await TrialFactsAsync(fixture, owner.ShopId));

        var stock = await IntakeAsync(owner);
        await SellAsync(owner, stock, Guid.NewGuid());
        await AssertStockAsync(owner, stock, 1);
    }

    private async Task RecoverOlderDeliveryAsync(P0213Fixture fixture, Merchant owner, EntitlementSnapshotV1 older,
        EntitlementSnapshotV1 active)
    {
        await DiscardSnapshotResponseAsync(fixture, older, owner.ShopId);
        var times = await EvidenceTimesAsync(fixture, older.EventId);
        Assert.NotNull(times);
        var retained = await ReadRetainedReceiptAsync(fixture, older.EventId);
        Assert.Equal("Superseded", retained.Disposition);
        Assert.Equal("Pending", await MerchantOutboxAsync(fixture, older.EventId));
        await AssertHeadsAsync(active);

        fixture.Transport.LeaseClock.Now += TimeSpan.FromSeconds(5);
        await fixture.DrainAsync(source: true);
        var receipt = await SendReceiptAsync(fixture, older);
        Assert.Equal(retained, receipt);
        Assert.Equal(active.Shop!.Revision, receipt.AppliedShopRevision);
        Assert.Equal(active.Account!.Revision, receipt.AppliedAccountRevision);
        Assert.Equal(times, await EvidenceTimesAsync(fixture, older.EventId));
        await AssertHeadsAsync(active);
    }

    private static async Task<EntitlementAcknowledgementV1> ReadRetainedReceiptAsync(P0213Fixture fixture, Guid eventId)
    {
        var bytes = Assert.IsType<byte[]>(await fixture.MerchantScalarAsync($"""
            SELECT acknowledgement FROM entitlement_snapshot_inbox WHERE event_id = '{eventId:D}'
            """));
        return JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(bytes, EntitlementSnapshotsV1.Json)!;
    }

    private static async Task AssertConfirmedAsync(P0213Fixture fixture, Guid eventId)
    {
        Assert.Equal("Applied", await OutboxAsync(fixture, eventId));
        Assert.Equal("Delivered", await MerchantOutboxAsync(fixture, eventId));
        Assert.Equal(1L, await fixture.SourceScalarAsync($"""
            SELECT count(*) FROM entitlement_merchant_inbox WHERE event_id = '{eventId:D}' AND kind = 'Acknowledgement'
            """));
    }

    private async Task AssertStaleThenRefreshAsync(P0213Fixture fixture, Merchant owner, AddInventoryResultDto stock,
        EntitlementSnapshotV1 active, DateTimeOffset end)
    {
        var (shop, account) = await AssertHeadsAsync(active);
        var earliest = shop.UpstreamVerifiedAt < account.UpstreamVerifiedAt ? shop.UpstreamVerifiedAt : account.UpstreamVerifiedAt;
        var boundary = earliest + EntitlementSnapshotsV1.Freshness;
        Assert.Equal(LicenseProjectionDecision.VerifiedPermission, Evaluate(shop, account, boundary.AddMicroseconds(-1)));
        Assert.Equal(LicenseProjectionDecision.Stale, Evaluate(shop, account, boundary));
        fixture.Hosts.StatusClock.Now = boundary.AddMicroseconds(1);
        Assert.Equal(LicenseProjectionDecision.Stale, Evaluate(shop, account, fixture.Hosts.StatusClock.Now));
        await AssertTrialStatusAsync(owner, end, "Active");
        await AssertSynchronizationAsync(fixture, owner, active, "Applied", "Acknowledged", active);
        await SellAsync(owner, stock, Guid.NewGuid());
        await AssertStockAsync(owner, stock, 2);

        await fixture.ScanAsync(source: true);
        var refreshed = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.True(refreshed.Shop!.Revision > active.Shop!.Revision);
        Assert.True(refreshed.Account!.Revision > active.Account!.Revision);
        await AssertSynchronizationAsync(fixture, owner, refreshed, "Pending", "Queued", active);
        await ConfirmAsync(fixture);
        var (newShop, newAccount) = await AssertHeadsAsync(refreshed);
        Assert.Equal(end, newShop.AccessEndsAt);
        Assert.Equal(LicenseProjectionDecision.VerifiedPermission, Evaluate(newShop, newAccount, fixture.Hosts.StatusClock.Now));
        await AssertSynchronizationAsync(fixture, owner, refreshed, "Applied", "Acknowledged", refreshed);
    }

    private static async Task<Guid> LinkAsync(P0213Fixture fixture, Merchant owner)
    {
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        Assert.Equal(0L, await fixture.Trial.CountAsync("trial_history"));
        return account;
    }

    private static async Task<ShopTrialOperationV1> StartTrialAsync(Merchant owner, ShopTrialAcceptanceV1 operation)
    {
        var trial = await P0210Tests.StartAndWaitAsync(owner, operation);
        Assert.Equal("Applied", trial.Status);
        Assert.Equal(P0210Fixture.Start, trial.AcceptedAt);
        Assert.Equal(P0210Fixture.Start.AddDays(14), trial.EndsAt);
        return trial;
    }

    private static async Task<string> TrialFactsAsync(P0213Fixture fixture, Guid shop)
    {
        Assert.Equal(1L, await fixture.Trial.CountAsync("trial_history"));
        Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_coverage_terms"));
        Assert.Equal(0L, await fixture.Trial.CountAsync("subscription_term_facts"));
        return Assert.IsType<string>(await fixture.SourceScalarAsync($"""
            SELECT jsonb_build_array(to_jsonb(t), to_jsonb(c))::text
            FROM trial_history t JOIN subscription_coverage_terms c USING (account_id, term_id)
            WHERE t.shop_id = '{shop:D}' AND t.development_fixture
            """));
    }

    private static async Task ConfirmAsync(P0213Fixture fixture)
    {
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
    }

    private async Task<(ShopLicenseState, AccountLicenseRestriction)> HeadsAsync(Guid shop, Guid account)
    {
        await using var db = MerchantDb(database);
        return (await db.Set<ShopLicenseState>().SingleAsync(row => row.ShopId == shop),
            await db.Set<AccountLicenseRestriction>().SingleAsync(row => row.AccountId == account));
    }

    private async Task<(ShopLicenseState, AccountLicenseRestriction)> AssertHeadsAsync(EntitlementSnapshotV1 expected)
    {
        var (shop, account) = await HeadsAsync(expected.ShopId, expected.AccountId);
        Assert.Null(shop.EnforcementActivatedAt);
        Assert.Equal(expected.Shop!.Revision, shop.Revision);
        Assert.Equal(expected.Account!.Revision, account.Revision);
        Assert.Equal(expected.Shop.UpstreamVerifiedAt, shop.UpstreamVerifiedAt);
        Assert.Equal(expected.Account.UpstreamVerifiedAt, account.UpstreamVerifiedAt);
        Assert.Equal(expected.Shop.State, shop.State);
        Assert.Equal(expected.Shop.AccessEndsAt, shop.AccessEndsAt);
        return (shop, account);
    }

    private static LicenseProjectionDecision Evaluate(ShopLicenseState shop, AccountLicenseRestriction account, DateTimeOffset now) =>
        LicenseProjectionPolicy.Evaluate(shop, account, now, EntitlementSnapshotsV1.Freshness);

    private static async Task AssertSynchronizationAsync(P0213Fixture fixture, Merchant owner, EntitlementSnapshotV1 saved,
        string state, string delivery, EntitlementSnapshotV1? applied)
    {
        using var response = await ShopStatusAsync(owner.Client, owner.ShopId);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var merchant = await ReadAsync(response);
        var provider = await P0212Fixture.ShopStatusAsync(fixture.Trial.CatalogueManager!, saved.AccountId, owner.ShopId);
        Assert.True(merchant.GetProperty("observeOnly").GetBoolean());
        Assert.True(provider.GetProperty("observeOnly").GetBoolean());
        Assert.Equal(state, merchant.GetProperty("entitlementSynchronization").GetString());
        Assert.Equal(state, provider.GetProperty("entitlementSynchronization").GetString());
        var sync = merchant.GetProperty("synchronization");
        Assert.True(JsonElement.DeepEquals(sync, provider.GetProperty("synchronization")));
        Assert.Equal(delivery, sync.GetProperty("delivery").GetString());
        Assert.Equal(saved.Account!.Revision, sync.GetProperty("savedAccountRevision").GetInt64());
        Assert.Equal(saved.Shop!.Revision, sync.GetProperty("savedShopRevision").GetInt64());
        Assert.Equal(applied?.Account!.Revision, sync.GetProperty("appliedAccountRevision").Deserialize<long?>());
        Assert.Equal(applied?.Shop!.Revision, sync.GetProperty("appliedShopRevision").Deserialize<long?>());
    }

    private static async Task AssertTrialStatusAsync(Merchant owner, DateTimeOffset end, string state)
    {
        using var response = await ShopStatusAsync(owner.Client, owner.ShopId);
        var status = await ReadAsync(response);
        Assert.Equal(state, status.GetProperty("state").GetString());
        Assert.True(status.GetProperty("observeOnly").GetBoolean());
        var term = status.GetProperty("term");
        Assert.Equal("Trial", term.GetProperty("basis").GetString());
        Assert.Equal(end, term.GetProperty("accessEndsAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, term.GetProperty("graceEndsAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, term.GetProperty("commercial").ValueKind);
    }

    private static async Task AssertStaffDeniedAsync(Merchant staff)
    {
        using var denied = await staff.Client.PutAsJsonAsync("/api/expiry-policies", new
        {
            expectedCatalogVersion = 1, reason = "Fixture staff attempt",
            policy = new { mode = "Block", expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false },
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private static async Task<AddInventoryResultDto> IntakeAsync(Merchant owner)
    {
        using var response = await owner.Client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "P0217 fixture stock", barcode = Guid.NewGuid().ToString("N"), uom = "pcs", batchNumber = "p0217",
            quantity = 5m, totalPurchaseCost = 50m, mrp = 12m, salesPrice = 12m, taxRatePercent = 0m,
            taxIncluded = false, purchaseTaxIncluded = false,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AddInventoryResultDto>())!;
    }

    private async Task<Guid> SellAsync(Merchant owner, AddInventoryResultDto stock, Guid key)
    {
        using var response = await owner.Client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = key.ToString("N"), customerName = "Walk-in fixture", customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash, paidAmount = 12m, dueAmount = 0m,
            items = new[] { new
            {
                stock.Barcode, stock.BatchNumber, stock.ItemName, stock.InventoryBatchId,
                quantity = 1m, costPrice = 10m, salesPrice = 12m, mrp = 12m, taxRatePercent = 0m, isPriceIncludingTax = false,
            } },
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var saleId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saleId").GetGuid();
        await using var db = MerchantDb(database);
        var sale = await db.Sales.SingleAsync(row => row.Id == saleId && row.ShopId == owner.ShopId);
        Assert.Equal(12m, sale.PaidAmount);
        Assert.Equal(12m, sale.TotalAmount);
        Assert.Equal(0m, sale.DueAmount);
        Assert.Equal(0m, sale.TotalTaxAmount);
        Assert.Equal(PaymentMethod.Cash, sale.PaymentMethod);
        Assert.NotNull(sale.InvoiceDocument);
        Assert.Equal(2, sale.InvoiceDocument.Version);
        Assert.Equal(InvoiceDocumentType.Receipt, sale.InvoiceDocument.DocumentType);
        Assert.Equal(sale.InvoiceNumber, sale.InvoiceDocument.Number);
        var movement = await db.StockTransactions.SingleAsync(row => row.ShopId == owner.ShopId &&
            row.ReferenceNumber == sale.InvoiceNumber && row.InventoryBatchId == stock.InventoryBatchId);
        Assert.Equal(StockTransactionType.Out, movement.TransactionType);
        Assert.Equal(-1m, movement.Quantity);
        return saleId;
    }

    private async Task AssertStockAsync(Merchant owner, AddInventoryResultDto stock, int sales)
    {
        await using var db = MerchantDb(database);
        Assert.Equal(sales, await db.Sales.CountAsync(row => row.ShopId == owner.ShopId));
        Assert.Equal(sales, await db.StockTransactions.CountAsync(row => row.InventoryBatchId == stock.InventoryBatchId &&
            row.TransactionType == StockTransactionType.Out));
        var batch = await db.InventoryBatches.SingleAsync(row => row.Id == stock.InventoryBatchId);
        Assert.Equal(stock.BatchQuantity - sales, batch.Quantity);
    }
}
