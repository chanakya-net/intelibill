using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Returns end to end: what the API posts as each return's accounting snapshot, what the legacy
/// backfill makes of returns recorded without one, and that Tally, XLSX and the P&amp;L report the
/// same adjustments in the period each was posted.
/// </summary>
[Collection("Integration Tests")]
public sealed class ReturnAccountingExportTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private static readonly DateOnly June1 = new(2026, 6, 1);
    private static readonly DateOnly June30 = new(2026, 6, 30);
    private static readonly DateOnly July1 = new(2026, 7, 1);
    private static readonly DateOnly July31 = new(2026, 7, 31);

    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public async Task InitializeAsync() => await _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PartialRefund_UsesApprovedAdjustment()
    {
        using var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop);

        var returnId = await RecordReturnAsync(shop, sale, ReturnPayoutDestination.Refund, Line(sale.LineId, 59m, "Opened pack"));

        var stored = await LoadReturnAsync(returnId);
        Assert.Equal(ReturnAccountingStatus.Balanced, stored.AccountingStatus);
        Assert.Equal("proportional-line-tax/1", stored.AccountingPolicyVersion);
        Assert.Equal(118m, stored.OriginalReturnedGross);
        Assert.Equal((50m, 9m, 0m), Split(stored));
        var leg = Assert.Single(stored.Settlements);
        Assert.Equal((ReturnSettlementKind.ExternalRefund, 59m), (leg.Kind, leg.Amount));

        var voucher = Assert.Single(await TallyVouchersAsync(shop, Today, Today), v => v.Type == "Credit Note");
        Assert.Equal(stored.ReturnNumber, voucher.Number);
        Assert.Equal(sale.InvoiceNumber, voucher.Reference);
        Assert.Equal(
            [("Sales", 50m, true), ("Output GST 18%", 9m, true), ("Customer Refunds Clearing", 59m, false)],
            voucher.Lines.ToArray());
    }

    [Fact]
    public async Task MixedDueAndStoreCredit_PreservesBothLegs()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var sale = await SellGoodsAsync(shop, customerId, paid: 18m);

        var returnId = await RecordReturnAsync(shop, sale, ReturnPayoutDestination.CreditNote, Line(sale.LineId));

        var stored = await LoadReturnAsync(returnId);
        var (creditNoteId, ledgerEntryId) = await QueryAsync(async db => (
            await db.CreditNotes.Where(n => n.SaleReturnId == returnId).Select(n => n.Id).SingleAsync(),
            await db.CustomerLedgerEntries
                .Where(e => e.SaleId == sale.SaleId && e.EntryType == CustomerLedgerEntryType.ReturnCredit)
                .Select(e => e.Id)
                .SingleAsync()));
        Assert.Equal(ReturnAccountingStatus.Balanced, stored.AccountingStatus);
        Assert.Collection(
            stored.Settlements.OrderBy(s => s.Sequence),
            receivable =>
            {
                Assert.Equal((ReturnSettlementKind.ReceivableReduction, 100m), (receivable.Kind, receivable.Amount));
                Assert.Equal(customerId, receivable.CustomerId);
                Assert.Equal(ledgerEntryId, receivable.CustomerLedgerEntryId);
            },
            storeCredit =>
            {
                Assert.Equal((ReturnSettlementKind.StoreCreditIssued, 18m), (storeCredit.Kind, storeCredit.Amount));
                Assert.Equal(creditNoteId, storeCredit.CreditNoteId);
            });

        var voucher = Assert.Single(await TallyVouchersAsync(shop, Today, Today), v => v.Type == "Credit Note");
        Assert.Contains(("Due Customer", 100m, false), voucher.Lines);
        Assert.Contains(("Credit Note Liability", 18m, false), voucher.Lines);
    }

    [Fact]
    public async Task ZeroRefund_HasNoFinancialVoucherButPreservesStockFact()
    {
        using var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop);
        var stockBefore = await BatchQuantityAsync(sale.BatchId);

        var returnId = await RecordReturnAsync(shop, sale, destination: null, Line(sale.LineId, 0m, "Goodwill exchange, no refund"));

        var stored = await LoadReturnAsync(returnId);
        Assert.Equal(ReturnAccountingStatus.Balanced, stored.AccountingStatus);
        Assert.Equal((0m, 0m, 0m), Split(stored));
        Assert.Empty(stored.Settlements);
        Assert.Equal(stockBefore + 1m, await BatchQuantityAsync(sale.BatchId));

        var vouchers = await TallyVouchersAsync(shop, Today, Today);
        Assert.DoesNotContain(vouchers, v => v.Number.StartsWith(stored.ReturnNumber, StringComparison.Ordinal));
        Assert.Single(vouchers, v => v.Type == "Sales");

        var adjustments = await XlsxAdjustmentRowsAsync(shop, Today, Today);
        var row = Assert.Single(adjustments);
        Assert.Equal(stored.ReturnNumber, row.ReturnNumber);
        Assert.Equal(118m, row.OriginalReturnedValue);
        Assert.Equal(0m, row.FinancialAdjustment);
    }

    [Fact]
    public async Task RepeatedFractionalReturns_AllocateFinalPaiseResidual()
    {
        using var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop, quantity: 3m, salesPrice: 10.01m);
        Assert.Equal((35.44m, 5.41m), await QueryAsync(async db =>
        {
            var stored = await db.Sales.AsNoTracking().SingleAsync(s => s.Id == sale.SaleId);
            return (stored.TotalAmount, stored.TotalTaxAmount);
        }));

        var returns = new List<SaleReturn>();
        for (var i = 0; i < 3; i++)
            returns.Add(await LoadReturnAsync(await RecordReturnAsync(shop, sale, ReturnPayoutDestination.Refund, Line(sale.LineId, quantity: 1m))));

        Assert.All(returns, r => Assert.Equal(ReturnAccountingStatus.Balanced, r.AccountingStatus));
        Assert.Equal([11.81m, 11.82m, 11.81m], returns.Select(r => r.TotalRefundAmount).ToArray());
        Assert.Equal([1.80m, 1.81m, 1.80m], returns.Select(r => r.AdjustmentTaxAmount!.Value).ToArray());
        Assert.Equal(35.44m, returns.Sum(r => r.TotalRefundAmount));
        Assert.Equal(5.41m, returns.Sum(r => r.AdjustmentTaxAmount!.Value));
        Assert.Equal(30.03m, returns.Sum(r => r.AdjustmentTaxableAmount!.Value));

        var creditNotes = (await TallyVouchersAsync(shop, Today, Today)).Where(v => v.Type == "Credit Note").ToList();
        Assert.Equal(3, creditNotes.Count);
        Assert.Equal(5.41m, creditNotes.SelectMany(v => v.Lines).Where(l => l.Ledger == "Output GST 18%").Sum(l => l.Amount));
    }

    [Fact]
    public async Task UnsupportedTaxAdjustment_BlocksPostingOrExportExplicitly()
    {
        using var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop);
        var returnId = await RecordReturnAsync(shop, sale, ReturnPayoutDestination.Refund, Line(sale.LineId));

        // A legacy line whose recorded tax share exceeds its gross: no proportion to apply.
        await ResetToLegacyAsync(returnId);
        await ExecuteAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sale_return_items SET tax_amount = max_refund_amount + 1 WHERE sale_return_id = {returnId}"));
        await RunBackfillAsync();

        var stored = await LoadReturnAsync(returnId);
        Assert.Equal(ReturnAccountingStatus.NeedsReview, stored.AccountingStatus);
        Assert.Contains("tax", stored.AccountingReviewReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(stored.AdjustmentTaxableAmount);
        Assert.Empty(stored.Settlements);

        using var tally = await ExportAsync(shop, "tallyXml", Today, Today);
        await AssertRefusedAsync(tally, HttpStatusCode.Conflict, "Export.ReturnNeedsAccountingReview");

        var row = Assert.Single(await XlsxAdjustmentRowsAsync(shop, Today, Today));
        Assert.Equal("Needs review", row.Status);
        Assert.Equal(stored.AccountingReviewReason, row.ReviewReason);
    }

    [Fact]
    public async Task UnbalancedLegacySnapshot_ProducesReviewDiagnostic()
    {
        using var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop);
        var returnId = await RecordReturnAsync(shop, sale, ReturnPayoutDestination.Refund, Line(sale.LineId));

        // Recorded before payout destinations were kept: where the ₹118 went is not evidenced.
        await ResetToLegacyAsync(returnId);
        await ExecuteAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sale_returns SET payout_destination = NULL WHERE id = {returnId}"));

        Assert.True(await RunBackfillAsync() >= 1);
        Assert.Equal(0, await RunBackfillAsync());

        var stored = await LoadReturnAsync(returnId);
        Assert.Equal(ReturnAccountingStatus.NeedsReview, stored.AccountingStatus);
        Assert.Equal("backfill:proportional-line-tax/1", stored.AccountingPolicyVersion);
        Assert.Contains("no recorded destination", stored.AccountingReviewReason, StringComparison.Ordinal);
        Assert.Empty(stored.Settlements);
        Assert.All(stored.Items, item => Assert.Null(item.AdjustmentTaxableAmount));

        using var tally = await ExportAsync(shop, "tallyXml", Today, Today);
        await AssertRefusedAsync(tally, HttpStatusCode.Conflict, "Export.ReturnNeedsAccountingReview");

        var report = await ProfitLossAsync(shop, Today, Today);
        var returnRow = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.True(returnRow.GetProperty("needsAccountingReview").GetBoolean());
    }

    [Fact]
    public async Task LegacyEvidencedReturn_BackfillsBalanced()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var sale = await SellGoodsAsync(shop, customerId, paid: 50m);
        var returnId = await RecordReturnAsync(shop, sale, ReturnPayoutDestination.CreditNote, Line(sale.LineId, 100m, "Seal broken"));
        var posted = await LoadReturnAsync(returnId);

        await ResetToLegacyAsync(returnId);
        Assert.True(await RunBackfillAsync() >= 1);

        var backfilled = await LoadReturnAsync(returnId);
        Assert.Equal(ReturnAccountingStatus.Balanced, backfilled.AccountingStatus);
        Assert.Equal("backfill:proportional-line-tax/1", backfilled.AccountingPolicyVersion);
        Assert.Null(backfilled.AccountingReviewReason);
        Assert.Equal(
            (posted.AdjustmentTaxableAmount, posted.AdjustmentTaxAmount, posted.AdjustmentNonTaxAmount),
            (backfilled.AdjustmentTaxableAmount, backfilled.AdjustmentTaxAmount, backfilled.AdjustmentNonTaxAmount));
        Assert.Equal(
            posted.Items.Select(i => (i.AdjustmentTaxableAmount, i.AdjustmentTaxAmount, i.AdjustmentNonTaxAmount)),
            backfilled.Items.Select(i => (i.AdjustmentTaxableAmount, i.AdjustmentTaxAmount, i.AdjustmentNonTaxAmount)));
        Assert.Equal(
            posted.Settlements.OrderBy(s => s.Sequence).Select(s => (s.Sequence, s.Kind, s.Amount, s.CustomerId, s.CreditNoteId, s.CustomerLedgerEntryId)),
            backfilled.Settlements.OrderBy(s => s.Sequence).Select(s => (s.Sequence, s.Kind, s.Amount, s.CustomerId, s.CreditNoteId, s.CustomerLedgerEntryId)));

        var voucher = Assert.Single(await TallyVouchersAsync(shop, Today, Today), v => v.Type == "Credit Note");
        Assert.Contains(("Sales", 84.75m, true), voucher.Lines);
        Assert.Contains(("Output GST 18%", 15.25m, true), voucher.Lines);
        Assert.Contains(("Due Customer", 68m, false), voucher.Lines);
        Assert.Contains(("Credit Note Liability", 32m, false), voucher.Lines);
    }

    [Fact]
    public async Task Void_ProducesLinkedReversalInItsOwnPeriod()
    {
        using var shop = await OpenShopAsync();
        var sale = await SellGoodsAsync(shop);
        var returnId = await RecordReturnAsync(shop, sale, ReturnPayoutDestination.Refund, Line(sale.LineId));
        using (var voided = await SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{returnId}/void", new { reason = "Wrong invoice" }))
            Assert.Equal(HttpStatusCode.NoContent, voided.StatusCode);

        // Sold and returned in June, voided in July.
        await ExecuteAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sales SET sold_at = {new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero)} WHERE id = {sale.SaleId}"));
        await ExecuteAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sale_returns SET processed_at = {new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero)}, voided_at = {new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero)} WHERE id = {returnId}"));
        var returnNumber = (await LoadReturnAsync(returnId)).ReturnNumber;

        var june = await TallyVouchersAsync(shop, June1, June30);
        var creditNote = Assert.Single(june, v => v.Type == "Credit Note");
        Assert.Equal(returnNumber, creditNote.Number);
        Assert.Equal("20260612", creditNote.Date);
        Assert.DoesNotContain(june, v => v.Type == "Journal");

        var july = await TallyVouchersAsync(shop, July1, July31);
        var reversal = Assert.Single(july);
        Assert.Equal(("Journal", $"{returnNumber}-VOID", "20260702"), (reversal.Type, reversal.Number, reversal.Date));
        Assert.Equal((returnNumber, "20260612"), (reversal.Reference, reversal.ReferenceDate));
        Assert.Equal(
            creditNote.Lines.Select(l => (l.Ledger, l.Amount, !l.IsDebit)).OrderBy(l => l.Ledger),
            reversal.Lines.OrderBy(l => l.Ledger));

        var julyReport = await ProfitLossAsync(shop, July1, July31);
        var reversalRow = Assert.Single(julyReport.GetProperty("items").EnumerateArray());
        Assert.True(reversalRow.GetProperty("isReversal").GetBoolean());
        Assert.Equal(118m, reversalRow.GetProperty("revenueAfterTax").GetDecimal());
        Assert.Equal(0, julyReport.GetProperty("summary").GetProperty("returnCount").GetInt32());

        var juneRows = await XlsxAdjustmentRowsAsync(shop, June1, June30);
        Assert.Equal([("Return", 118m)], juneRows.Select(r => (r.Entry, r.FinancialAdjustment)).ToArray());
        var julyRows = await XlsxAdjustmentRowsAsync(shop, July1, July31);
        Assert.Equal([("Void reversal", -118m)], julyRows.Select(r => (r.Entry, r.FinancialAdjustment)).ToArray());
    }

    [Fact]
    public async Task AllFormats_AgreeOnAdjustmentTotals()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var cashSale = await SellGoodsAsync(shop);
        var creditSale = await SellGoodsAsync(shop, customerId, paid: 18m);
        var partialSale = await SellGoodsAsync(shop);
        await RecordReturnAsync(shop, cashSale, ReturnPayoutDestination.Refund, Line(cashSale.LineId));
        await RecordReturnAsync(shop, creditSale, ReturnPayoutDestination.CreditNote, Line(creditSale.LineId));
        await RecordReturnAsync(shop, partialSale, ReturnPayoutDestination.Refund, Line(partialSale.LineId, 59m, "Opened pack"));

        var vouchers = await TallyVouchersAsync(shop, Today, Today);
        var tallyReturns = vouchers.Where(v => v.Type == "Credit Note").Sum(v => v.Lines.Where(l => !l.IsDebit).Sum(l => l.Amount));
        Assert.All(vouchers, v => Assert.Equal(
            v.Lines.Where(l => l.IsDebit).Sum(l => l.Amount),
            v.Lines.Where(l => !l.IsDebit).Sum(l => l.Amount)));

        var xlsxReturns = (await XlsxAdjustmentRowsAsync(shop, Today, Today)).Sum(r => r.FinancialAdjustment);

        var report = await ProfitLossAsync(shop, Today, Today);
        var profitLossReturns = -report.GetProperty("summary").GetProperty("revenueIncludingTax").GetDecimal();

        Assert.Equal(295m, tallyReturns);
        Assert.Equal(295m, xlsxReturns);
        Assert.Equal(295m, profitLossReturns);

        using var pdf = await ExportAsync(shop, "pdf", Today, Today);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
    }

    // ── Arrange ─────────────────────────────────────────────────────────────────

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Shop(HttpClient Client, string Token) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record SoldSale(Guid SaleId, string InvoiceNumber, Guid LineId, Guid BatchId);

    private sealed record TallyVoucher(
        string Number,
        string Type,
        string Date,
        string Reference,
        string ReferenceDate,
        IReadOnlyList<(string Ledger, decimal Amount, bool IsDebit)> Lines);

    private sealed record AdjustmentRow(
        string Entry,
        string ReturnNumber,
        decimal OriginalReturnedValue,
        decimal FinancialAdjustment,
        string Status,
        string ReviewReason);

    private async Task<Shop> OpenShopAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var enrolled = await client.EnrollAsync(new
        {
            email = $"return-accounting-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Return",
            lastName = "Owner",
        });
        enrolled.EnsureSuccessStatusCode();
        var token = (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        using var created = await client.SendAsync(request);
        created.EnsureSuccessStatusCode();
        var shopToken = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        return new Shop(client, shopToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(Shop shop, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", shop.Token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await shop.Client.SendAsync(request);
    }

    private static async Task<Guid> AddCustomerAsync(Shop shop)
    {
        using var customer = await SendAsync(shop, HttpMethod.Post, "/api/customers", new
        {
            name = "Due Customer",
            phoneNumber = "+919812345678",
            address = (string?)null,
            isActive = true,
        });
        customer.EnsureSuccessStatusCode();
        return (await customer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customerId").GetGuid();
    }

    /// <summary>One goods line at the given price + 18% GST, paid in cash or partly on the customer's account.</summary>
    private async Task<SoldSale> SellGoodsAsync(
        Shop shop,
        Guid? customerId = null,
        decimal? paid = null,
        decimal quantity = 1m,
        decimal salesPrice = 100m)
    {
        var barcode = $"RA-{Guid.NewGuid():N}";
        var itemName = $"Return Item {barcode[3..11]}";
        var unitCost = Math.Round(salesPrice * 0.8m, 2, MidpointRounding.AwayFromZero);
        using var stock = await SendAsync(shop, HttpMethod.Post, "/api/inventory/inbound", new
        {
            itemName,
            barcode,
            itemDescription = (string?)null,
            hsnCode = (string?)null,
            uom = "pcs",
            batchNumber = "B-001",
            quantity = 10m,
            totalPurchaseCost = unitCost * 10m,
            mrp = 120m,
            salesPrice,
            taxRatePercent = 18m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        stock.EnsureSuccessStatusCode();
        var batchId = (await stock.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inventoryBatchId").GetGuid();

        var taxable = Math.Round(quantity * salesPrice, 2, MidpointRounding.AwayFromZero);
        var total = taxable + Math.Round(taxable * 0.18m, 2, MidpointRounding.AwayFromZero);
        var paidAmount = paid ?? total;
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId,
            customerName = customerId.HasValue ? "Due Customer" : "Walk-in Customer",
            customerPhone = customerId.HasValue ? "+919812345678" : "+919876543210",
            paymentMethod = paidAmount < total ? (int)PaymentMethod.Credit : (int)PaymentMethod.Cash,
            paidAmount,
            dueAmount = total - paidAmount,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName,
                    quantity,
                    costPrice = unitCost,
                    salesPrice,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, content);
        var saleId = JsonSerializer.Deserialize<JsonElement>(content).GetProperty("saleId").GetGuid();

        return await QueryAsync(async db =>
        {
            var sale = await db.Sales.AsNoTracking().Include(s => s.Items).SingleAsync(s => s.Id == saleId);
            return new SoldSale(sale.Id, sale.InvoiceNumber, sale.Items.Single().Id, batchId);
        });
    }

    private static object Line(Guid saleItemId, decimal? approvedRefundAmount = null, string? notes = null, decimal quantity = 1m) => new
    {
        saleItemId,
        quantity,
        lineType = (int)SaleLineType.Goods,
        condition = (int)SaleReturnCondition.Restockable,
        approvedRefundAmount,
        notes,
    };

    /// <summary>Records a return through the API and answers its id.</summary>
    private async Task<Guid> RecordReturnAsync(Shop shop, SoldSale sale, ReturnPayoutDestination? destination, object line)
    {
        var earlier = await QueryAsync(db => db.SaleReturns.Where(r => r.SaleId == sale.SaleId).Select(r => r.Id).ToListAsync());
        using var response = await SendAsync(shop, HttpMethod.Post, $"/api/sales/{sale.SaleId}/returns", new
        {
            payoutDestination = (int?)destination,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = (string?)null,
            creditNoteReason = destination == ReturnPayoutDestination.CreditNote ? "Store credit" : null,
            items = new[] { line },
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);

        return await QueryAsync(db => db.SaleReturns
            .Where(r => r.SaleId == sale.SaleId && !earlier.Contains(r.Id))
            .Select(r => r.Id)
            .SingleAsync());
    }

    /// <summary>The return as an API version before accounting snapshots left it.</summary>
    private async Task ResetToLegacyAsync(Guid returnId)
    {
        await ExecuteAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM sale_return_settlements WHERE sale_return_id = {returnId}"));
        await ExecuteAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE sale_return_items
            SET adjustment_taxable_amount = NULL, adjustment_tax_amount = NULL, adjustment_non_tax_amount = NULL
            WHERE sale_return_id = {returnId}
            """));
        await ExecuteAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE sale_returns
            SET accounting_status = NULL, accounting_policy_version = NULL, accounting_review_reason = NULL,
                adjustment_taxable_amount = NULL, adjustment_tax_amount = NULL, adjustment_non_tax_amount = NULL
            WHERE id = {returnId}
            """));
    }

    private async Task<int> RunBackfillAsync()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "scripts", "db", "backfill-sale-return-accounting.sql"));
        return await QueryAsync(db => db.Database.ExecuteSqlRawAsync(script));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // ── Exports ─────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> ExportAsync(Shop shop, string format, DateOnly from, DateOnly to, string level = "summary") =>
        SendAsync(shop, HttpMethod.Get, $"/api/exports/sales?format={format}&level={level}&startDate={from:yyyy-MM-dd}&endDate={to:yyyy-MM-dd}");

    private static async Task<IReadOnlyList<TallyVoucher>> TallyVouchersAsync(Shop shop, DateOnly from, DateOnly to)
    {
        using var response = await ExportAsync(shop, "tallyXml", from, to);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);

        return XDocument.Parse(content)
            .Descendants("VOUCHER")
            .Select(v => new TallyVoucher(
                (string)v.Element("VOUCHERNUMBER")!,
                (string)v.Element("VOUCHERTYPE")!,
                (string)v.Element("DATE")!,
                (string)v.Element("REFERENCE")!.Element("REFERENCENUMBER")!,
                (string)v.Element("REFERENCE")!.Element("REFERENCEDATE")!,
                v.Elements("VOUCHERLINE")
                    .Select(l => (
                        (string)l.Element("LEDGER")!,
                        decimal.Parse((string)l.Element("AMOUNT")!, CultureInfo.InvariantCulture),
                        (string)l.Element("ISDEBIT")! == "Yes"))
                    .ToList()))
            .ToList();
    }

    private static async Task<IReadOnlyList<AdjustmentRow>> XlsxAdjustmentRowsAsync(Shop shop, DateOnly from, DateOnly to)
    {
        using var response = await ExportAsync(shop, "xlsx", from, to);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Return Adjustments");
        var header = sheet.RowsUsed().First(r => r.Cell(1).GetString() == "Posting Date").RowNumber();

        var rows = new List<AdjustmentRow>();
        for (var row = header + 1; !sheet.Cell(row, 3).IsEmpty(); row++)
        {
            rows.Add(new AdjustmentRow(
                sheet.Cell(row, 2).GetString(),
                sheet.Cell(row, 3).GetString(),
                sheet.Cell(row, 6).GetValue<decimal>(),
                sheet.Cell(row, 7).GetValue<decimal>(),
                sheet.Cell(row, 15).GetString(),
                sheet.Cell(row, 17).GetString()));
        }

        return rows;
    }

    private static async Task<JsonElement> ProfitLossAsync(Shop shop, DateOnly from, DateOnly to)
    {
        using var response = await SendAsync(shop, HttpMethod.Get,
            $"/api/sales/profit-loss?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&type=saleReturn&pageSize=100");
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);
        return JsonSerializer.Deserialize<JsonElement>(content);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {content}");
        Assert.Contains(code, content, StringComparison.Ordinal);
    }

    // ── Reads ───────────────────────────────────────────────────────────────────

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<int> ExecuteAsync(Func<ApplicationDbContext, Task<int>> command) => QueryAsync(command);

    private Task<SaleReturn> LoadReturnAsync(Guid returnId) =>
        QueryAsync(db => db.SaleReturns
            .AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.Settlements)
            .SingleAsync(r => r.Id == returnId));

    private static (decimal Taxable, decimal Tax, decimal NonTax) Split(SaleReturn saleReturn) =>
        (saleReturn.AdjustmentTaxableAmount!.Value, saleReturn.AdjustmentTaxAmount!.Value, saleReturn.AdjustmentNonTaxAmount!.Value);

    private Task<decimal> BatchQuantityAsync(Guid batchId) =>
        QueryAsync(db => db.InventoryBatches.Where(b => b.Id == batchId).Select(b => b.Quantity).SingleAsync());
}
