using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413bLicensingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restricted_prior_receipt_cancels_once_with_original_facts(bool tradingSuspension)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var receipt = await SeedReceiptAsync(host, client);
        var binding = await BindAsync(receipt.Shop);
        await ApplyAsync(binding, tradingSuspension ? Deadline.AddHours(1) : Deadline,
            restriction: tradingSuspension ? "Trading" : "Unrestricted",
            effective: tradingSuspension ? Deadline : null);
        await EnforceAsync(receipt.Shop);

        var original = await ReadOriginalAsync(receipt);
        _clock.Now = Deadline.AddTicks(-1);
        await AssertEligiblePreviewAsync(client, receipt, sourceOnly: false);
        _clock.Now = Deadline;
        await AssertEligiblePreviewAsync(client, receipt, sourceOnly: true);
        await using (var db = Open(Migrator))
            Assert.Empty(await db.PostingPeriods.Where(x => x.ShopId == receipt.Shop).ToListAsync(Ct));

        var command = receipt.Command();
        using var result = await client.PostAsJsonAsync(receipt.Path + "/cancellations", command, Ct);
        var first = await ReadSuccessAsync(result);
        using var replay = await client.PostAsJsonAsync(receipt.Path + "/cancellations", command, Ct);
        Assert.True(JsonElement.DeepEquals(first, await ReadSuccessAsync(replay)));
        await AssertPostedExactlyOnceAsync(receipt);
        Assert.Equal(original, await ReadOriginalAsync(receipt));
        _output.WriteLine($"PostgreSQL: restricted at {Deadline:O} ({(tradingSuspension ? "trading suspension" : "expiry")}); " +
            "eligible 100ns earlier and at the boundary, one cancellation, two full goods lines, ₹80 cash, ₹120 due; original sale business fields, issued JSON and lines identical.");
    }

    [Theory]
    [InlineData("tax-document", "TaxInvoiceBlocked")]
    [InlineData("tax-amount", "SnapshotMismatch")]
    [InlineData("offline", "OfflineOriginBlocked")]
    [InlineData("service", "ServiceBlocked")]
    [InlineData("external", "ExternalRefundWorkflowRequired")]
    [InlineData("prior-month", "OtherMonthBlocked")]
    [InlineData("future", "PriorSourceIneligible")]
    [InlineData("closed", "ClosedMonthBlocked")]
    public async Task Restricted_cancellation_preserves_receipt_refusals(string variant, string reason)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var soldAt = variant switch { "future" => Deadline.AddMinutes(1), "prior-month" => Deadline.AddMonths(-1), _ => _clock.Now };
        var receipt = await SeedReceiptAsync(host, client,
            method: variant == "external" ? PaymentMethod.Card : PaymentMethod.Cash,
            documentType: variant == "tax-document" ? InvoiceDocumentType.TaxInvoice : InvoiceDocumentType.Receipt,
            soldAt: soldAt, service: variant == "service", offline: variant == "offline",
            tax: variant == "tax-amount" ? 5m : 0m);
        await ExpireAsync(receipt);
        if (variant == "closed") await CloseMonthAsync(receipt);
        using var result = await client.PostAsync(receipt.Path + "/cancellation-preview", null, Ct);
        var preview = await ReadSuccessAsync(result);
        Assert.True(preview.GetProperty("sourceCompletionOnly").GetBoolean());
        Assert.False(preview.GetProperty("eligible").GetBoolean());
        Assert.Contains(preview.GetProperty("reasons").EnumerateArray(),
            x => x.GetProperty("code").GetString() == $"SaleCancellation.{reason}");
        await AssertProblemAsync(await client.PostAsJsonAsync(receipt.Path + "/cancellations", receipt.Command(), Ct),
            HttpStatusCode.Conflict, variant == "closed" ? "PostingPeriod.Closed" : $"SaleCancellation.{reason}");
        await AssertUnchangedAsync(receipt);
    }

    [Fact]
    public async Task Restricted_cancellation_preserves_confirmations_versions_and_both_rollout_switches()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var foreign = CreateClient(host);
        var receipt = await SeedReceiptAsync(host, client);
        await CreateShopAsync(foreign);
        await ExpireAsync(receipt);
        var route = receipt.Path + "/cancellations";
        await AssertProblemAsync(await client.PostAsJsonAsync(route, receipt.Command(goods: false), Ct),
            HttpStatusCode.BadRequest, "SaleCancellation.InvalidRequest");
        await AssertProblemAsync(await client.PostAsJsonAsync(route, receipt.Command(cash: false), Ct),
            HttpStatusCode.BadRequest, "SaleCancellation.CashConfirmationInvalid");
        await AssertProblemAsync(await client.PostAsJsonAsync(route, receipt.Command(version: receipt.Sale.Version + 1), Ct),
            HttpStatusCode.Conflict, "SaleCancellation.VersionConflict");

        var rollout = Rollout(host);
        rollout.Enabled = false;
        await AssertProblemAsync(await client.PostAsJsonAsync(route, receipt.Command(), Ct),
            HttpStatusCode.ServiceUnavailable, "SaleCancellation.PostingDisabled");
        rollout.Enabled = true;
        rollout.AllowedShopIds = [];
        await AssertProblemAsync(await client.PostAsJsonAsync(route, receipt.Command(), Ct),
            HttpStatusCode.ServiceUnavailable, "SaleCancellation.PostingDisabled");
        await AssertProblemAsync(await foreign.PostAsJsonAsync(route, receipt.Command(), Ct),
            HttpStatusCode.NotFound, "Sale.NotFound");
        await AssertUnchangedAsync(receipt);
    }

    [Fact]
    public async Task Confirmation_cannot_add_a_cash_refund_to_an_unpaid_receipt()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var receipt = await SeedReceiptAsync(host, client, paid: 0m);
        await ExpireAsync(receipt);
        await AssertProblemAsync(await client.PostAsJsonAsync(receipt.Path + "/cancellations", receipt.Command(), Ct),
            HttpStatusCode.BadRequest, "SaleCancellation.CashConfirmationInvalid");
        await AssertUnchangedAsync(receipt);
    }

    private async Task<Binding> ExpireAsync(Receipt receipt)
    {
        var binding = await BindAsync(receipt.Shop);
        await ApplyAsync(binding, Deadline);
        await EnforceAsync(receipt.Shop);
        _clock.Now = Deadline;
        return binding;
    }

    private async Task CloseMonthAsync(Receipt receipt)
    {
        await using var db = Open(Migrator);
        var period = await db.PostingPeriods.SingleOrDefaultAsync(x => x.ShopId == receipt.Shop, Ct);
        if (period is null)
        {
            period = PostingPeriod.Open(receipt.Shop, receipt.Sale.BusinessDate);
            db.Add(period);
        }
        Assert.False(period.Close(receipt.Actor, _clock.Now, period.EndDate.AddDays(1), "Fixture close").IsError);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task AssertEligiblePreviewAsync(HttpClient client, Receipt receipt, bool sourceOnly)
    {
        using var result = await client.PostAsync(receipt.Path + "/cancellation-preview", null, Ct);
        var preview = await ReadSuccessAsync(result);
        Assert.True(preview.GetProperty("eligible").GetBoolean(), preview.GetRawText());
        Assert.Equal(sourceOnly, preview.GetProperty("sourceCompletionOnly").GetBoolean());
        Assert.True(preview.GetProperty("postingEnabled").GetBoolean());
        Assert.Equal(80m, preview.GetProperty("cashRefundAmount").GetDecimal());
        Assert.Equal(120m, preview.GetProperty("dueReductionAmount").GetDecimal());
        Assert.Equal(2, preview.GetProperty("lines").GetArrayLength());
    }

    private async Task<string> ReadOriginalAsync(Receipt receipt)
    {
        await using var db = Open(Migrator);
        return await db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object('sale', to_jsonb(s) - 'status' - 'updated_at',
                'lines', (SELECT jsonb_agg(to_jsonb(i) ORDER BY i.id) FROM sale_items i WHERE i.sale_id = s.id))::text AS "Value"
            FROM sales s WHERE s.id = {receipt.Sale.Id}
            """).SingleAsync(Ct);
    }

    private async Task AssertPostedExactlyOnceAsync(Receipt receipt)
    {
        await using var db = Open(Migrator);
        var fact = await db.Set<SaleCancellation>().Include(x => x.Lines).SingleAsync(x => x.SaleId == receipt.Sale.Id, Ct);
        Assert.Equal(80m, fact.CashRefundAmount);
        Assert.Equal(120m, fact.DueReductionAmount);
        Assert.True(fact.CashRefundConfirmed && fact.GoodsRecoveredConfirmed);
        Assert.Equal(receipt.Sale.Items.Select(x => x.Id).Order(), fact.Lines.Select(x => x.SaleItemId).Order());
        Assert.All(fact.Lines, line => Assert.Equal(1m, line.Quantity));
        Assert.Equal(10m, (await db.InventoryBatches.SingleAsync(x => x.Id == receipt.Batch, Ct)).Quantity);
        Assert.Equal(2, await db.StockTransactions.CountAsync(x => x.ShopId == receipt.Shop &&
            x.TransactionType == StockTransactionType.SaleCancellation, Ct));
        Assert.Equal(1, await db.CustomerLedgerEntries.CountAsync(x => x.ShopId == receipt.Shop &&
            x.EntryType == CustomerLedgerEntryType.SaleCancellationCredit && x.Amount == 120m, Ct));
        Assert.Equal(1, await db.OperationReceipts.CountAsync(x => x.ShopId == receipt.Shop &&
            x.OperationType == FinancialOperationType.SaleCancellation && x.Status == OperationReceiptStatus.Completed, Ct));
    }
}
