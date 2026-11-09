using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Posting;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Which business day a posting belongs to: an Indian shop's day starts at 18:30 UTC, the sales
/// list, dashboard, P&amp;L and every export read the stored business date, and a later change of the
/// shop's time zone leaves posted dates alone.
/// </summary>
[Collection("Integration Tests")]
public sealed class BusinessPostingPeriodTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture, services =>
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<SettableClock>();
        services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<SettableClock>());
    });

    private SettableClock Clock => _factory.Services.GetRequiredService<SettableClock>();

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
    public async Task IstEarlyMorning_BelongsToLocalDay()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);

        Clock.Set(Utc(2026, 10, 20, 20, 30));
        await SellAsync(shop, stock);

        Assert.Single(await SalesListAsync(shop, Oct21, Oct21));
        Assert.Empty(await SalesListAsync(shop, Oct20, Oct20));

        using var dashboard = await GetJsonAsync(shop, "/api/dashboard?from=2026-10-20&to=2026-10-21");
        var buckets = dashboard.RootElement.GetProperty("salesTrendSeries").EnumerateArray()
            .Where(p => p.GetProperty("amount").GetDecimal() > 0m)
            .Select(p => p.GetProperty("date").GetString()!)
            .ToArray();
        Assert.Equal(["2026-10-21"], buckets);

        var vouchers = await TallyVouchersAsync(shop, Oct21, Oct21);
        Assert.Equal("20261021", Assert.Single(vouchers, v => v.Type == "Sales").Date);
        Assert.Empty(await TallyVouchersAsync(shop, Oct20, Oct20));
    }

    [Fact]
    public async Task ExactLocalMidnight_StartsNewDay()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);

        Clock.Set(Utc(2026, 10, 20, 18, 29, 59));
        var before = await SellAsync(shop, stock);
        Clock.Set(Utc(2026, 10, 20, 18, 30, 0));
        var after = await SellAsync(shop, stock);

        Assert.Equal([before], (await SalesListAsync(shop, Oct20, Oct20)).Select(s => s.InvoiceNumber).ToArray());
        Assert.Equal([after], (await SalesListAsync(shop, Oct21, Oct21)).Select(s => s.InvoiceNumber).ToArray());
        var both = (await SalesListAsync(shop, Oct20, Oct21)).Select(s => s.InvoiceNumber).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { before, after }.OrderBy(n => n).ToArray(), both);
    }

    [Fact]
    public async Task FiscalYearBoundary_SplitsAtIstMidnight()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);
        var customerId = await AddCustomerAsync(shop);

        Clock.Set(Utc(2026, 3, 31, 18, 29, 59));
        var march = await SellAsync(shop, stock, customerId, paid: 18m);
        Clock.Set(Utc(2026, 3, 31, 18, 30, 0));
        var april = await SellAsync(shop, stock, customerId, paid: 18m);

        Assert.Contains("/2526/", march, StringComparison.Ordinal);
        Assert.Contains("/2627/", april, StringComparison.Ordinal);

        var stored = await QueryAsync(async db =>
        {
            var sales = await db.Sales.AsNoTracking()
                .Where(s => s.InvoiceNumber == march || s.InvoiceNumber == april)
                .ToDictionaryAsync(s => s.InvoiceNumber);
            var ledger = await db.CustomerLedgerEntries.AsNoTracking()
                .Where(e => e.EntryType == CustomerLedgerEntryType.SaleDue)
                .ToListAsync();
            return (sales, ledger);
        });
        Assert.Equal(new DateOnly(2026, 3, 31), stored.sales[march].BusinessDate);
        Assert.Equal(new DateOnly(2026, 4, 1), stored.sales[april].BusinessDate);
        Assert.Equal(
            new[] { new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1) },
            stored.ledger.Select(e => e.EntryDate).OrderBy(d => d).ToArray());
    }

    [Fact]
    public async Task TimezoneChange_DoesNotRewritePostedDates()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);
        Clock.Set(Utc(2026, 10, 20, 20, 30));
        var invoice = await SellAsync(shop, stock);

        await using (var connection = new NpgsqlConnection(_factory.RuntimeConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var scope = new NpgsqlCommand(
                "SELECT set_config('app.active_shop_id', @shop, true), set_config('app.current_user_id', @user, true)",
                connection, transaction))
            {
                scope.Parameters.AddWithValue("shop", shop.ShopId.ToString());
                scope.Parameters.AddWithValue("user", shop.UserId.ToString());
                await scope.ExecuteNonQueryAsync();
            }

            await using var update = new NpgsqlCommand("UPDATE shops SET business_time_zone = 'UTC'", connection, transaction);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
            await transaction.CommitAsync();
        }

        Assert.Equal("UTC", await QueryAsync(db => db.Shops.AsNoTracking()
            .Where(s => s.Id == shop.ShopId).Select(s => s.BusinessTimeZoneId).SingleAsync()));

        Assert.Equal([invoice], (await SalesListAsync(shop, Oct21, Oct21)).Select(s => s.InvoiceNumber).ToArray());
        Assert.Empty(await SalesListAsync(shop, Oct20, Oct20));
        Assert.Equal("20261021", Assert.Single(await TallyVouchersAsync(shop, Oct21, Oct21), v => v.Type == "Sales").Date);

        using var report = await GetJsonAsync(shop, "/api/sales/profit-loss?from=2026-10-21&to=2026-10-21&pageSize=100");
        Assert.Equal(1, report.RootElement.GetProperty("summary").GetProperty("invoiceCount").GetInt32());

        Assert.Equal(new DateOnly(2026, 10, 21), await QueryAsync(db => db.Sales.AsNoTracking()
            .Where(s => s.InvoiceNumber == invoice).Select(s => s.BusinessDate).SingleAsync()));
    }

    [Fact]
    public async Task JuneSaleJulyReturn_ExportsInOwnPeriods()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);

        Clock.Set(Utc(2026, 6, 15, 6, 0));
        var invoice = await SellAsync(shop, stock);
        // 20:00Z on 30 June is 01:30 IST on 1 July: July in the shop's zone, still June in UTC.
        Clock.Set(Utc(2026, 6, 30, 20, 0));
        await ReturnAsync(shop, invoice);

        Assert.Equal([invoice], (await XlsxSaleNumbersAsync(shop, June1, June30)).ToArray());
        Assert.Empty(await XlsxAdjustmentRowsAsync(shop, June1, June30));

        Assert.Empty(await XlsxSaleNumbersAsync(shop, July1, July31));
        var july = Assert.Single(await XlsxAdjustmentRowsAsync(shop, July1, July31));
        Assert.Equal(("Return", invoice), (july.Entry, july.Invoice));
    }

    [Fact]
    public async Task AugustVoid_ReversesJulyReturnInAugust()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);

        Clock.Set(Utc(2026, 6, 15, 6, 0));
        var invoice = await SellAsync(shop, stock);
        // 20:00Z on 30 June is 01:30 IST on 1 July: July in the shop's zone, still June in UTC.
        Clock.Set(Utc(2026, 6, 30, 20, 0));
        var returnId = await ReturnAsync(shop, invoice);
        // 19:00Z on 31 July is 00:30 IST on 1 August: August in the shop's zone, still July in UTC.
        Clock.Set(Utc(2026, 7, 31, 19, 0));
        using (var voided = await SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{returnId}/void", new { reason = "Wrong invoice" }))
            Assert.Equal(HttpStatusCode.NoContent, voided.StatusCode);

        Assert.Equal(["Return"], (await XlsxAdjustmentRowsAsync(shop, July1, July31)).Select(r => r.Entry).ToArray());
        var august = Assert.Single(await XlsxAdjustmentRowsAsync(shop, Aug1, Aug31));
        Assert.Equal(("Void reversal", invoice), (august.Entry, august.Invoice));
        Assert.Empty(await XlsxAdjustmentRowsAsync(shop, June1, June30));
        Assert.Equal([invoice], (await XlsxSaleNumbersAsync(shop, June1, June30)).ToArray());
    }


    // ── Posting periods ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CloseAndReopen_OwnerOnly_Audited()
    {
        var shop = await OpenShopAsync();

        // Tokens are issued on the real clock; it only moves once both exist. A role change ends the old token, so the manager signs in to the shop again for a fresh one.
        await SetRoleAsync(shop, "Manager");
        var manager = await SwitchTokenAsync(shop);
        using (var refused = await SendAsync(manager, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition("June books final", "close-1")))
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using (var managerList = await SendAsync(manager, HttpMethod.Get, "/api/posting-periods?fiscalYear=2026"))
            Assert.Equal(HttpStatusCode.OK, managerList.StatusCode);
        await SetRoleAsync(shop, "Owner");
        shop = await SwitchTokenAsync(shop);
        Clock.Set(Utc(2026, 7, 5, 6, 0));

        using (var closed = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition("June books final", "close-1")))
        {
            var body = await closed.Content.ReadAsStringAsync();
            Assert.True(closed.StatusCode == HttpStatusCode.OK, body);
            var json = JsonDocument.Parse(body).RootElement;
            Assert.Equal("Closed", json.GetProperty("state").GetString());
            Assert.Equal("2026-06-01", json.GetProperty("startDate").GetString());
            Assert.Equal("2026-06-30", json.GetProperty("endDate").GetString());
        }

        using (var list = await GetJsonAsync(shop, "/api/posting-periods?fiscalYear=2026"))
        {
            var periods = list.RootElement.GetProperty("periods").EnumerateArray().ToList();
            Assert.Equal(2026, list.RootElement.GetProperty("fiscalYearStart").GetInt32());
            Assert.Equal(12, periods.Count);
            Assert.Equal("2026-04-01", periods[0].GetProperty("startDate").GetString());
            Assert.Equal("2027-03-31", periods[11].GetProperty("endDate").GetString());
            var june = periods[2];
            Assert.Equal("2026-06-01", june.GetProperty("startDate").GetString());
            Assert.Equal("Closed", june.GetProperty("state").GetString());
            Assert.Equal(shop.UserId, june.GetProperty("closedByUserId").GetGuid());
            var closeEvent = Assert.Single(june.GetProperty("events").EnumerateArray());
            Assert.Equal("Closed", closeEvent.GetProperty("action").GetString());
            Assert.Equal("June books final", closeEvent.GetProperty("reason").GetString());
            Assert.All(periods.Where((_, i) => i != 2), p => Assert.Equal("Open", p.GetProperty("state").GetString()));
        }

        using (var reopened = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/reopen", Transition("Missed invoice", "reopen-1")))
        {
            var body = await reopened.Content.ReadAsStringAsync();
            Assert.True(reopened.StatusCode == HttpStatusCode.OK, body);
            var json = JsonDocument.Parse(body).RootElement;
            Assert.Equal("Open", json.GetProperty("state").GetString());
            Assert.Equal(["Closed", "Reopened"], json.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("action").GetString()!).ToArray());
        }

        // The same key and content replays the committed answer without a third event.
        using (var replay = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/reopen", Transition("  Missed invoice ", "reopen-1")))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(2, JsonDocument.Parse(await replay.Content.ReadAsStringAsync()).RootElement.GetProperty("events").GetArrayLength());
        }

        using (var mismatch = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/reopen", Transition("A different reason", "reopen-1")))
        {
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            Assert.Contains("Operation.PayloadMismatch", await mismatch.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(2, await ScalarAsync<long>(
            "SELECT count(*) FROM posting_period_events WHERE shop_id = @shop", ("shop", shop.ShopId)));
    }

    [Fact]
    public async Task Close_RefusesBadInput()
    {
        using var shop = await OpenShopAsync();
        Clock.Set(Utc(2026, 7, 5, 6, 0));

        foreach (var period in new[] { "2026-13", "2026-6", "June", "2026-06-01" })
        {
            using var invalid = await SendAsync(shop, HttpMethod.Post, $"/api/posting-periods/{period}/close", Transition("reason", "k-" + period));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using (var blank = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition("   ", "blank")))
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        using (var longReason = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition(new string('x', 501), "long")))
            Assert.Equal(HttpStatusCode.BadRequest, longReason.StatusCode);
        using (var noKey = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", new { reason = "no key" }))
            Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);

        using (var notEnded = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-07/close", Transition("too early", "early")))
        {
            Assert.Equal(HttpStatusCode.Conflict, notEnded.StatusCode);
            Assert.Contains("PostingPeriod.NotEnded", await notEnded.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var alreadyOpen = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/reopen", Transition("nothing to reopen", "open")))
        {
            Assert.Equal(HttpStatusCode.Conflict, alreadyOpen.StatusCode);
            Assert.Contains("PostingPeriod.AlreadyOpen", await alreadyOpen.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // Every refusal rolled back, so the failed attempts left neither a receipt nor an event.
        Assert.Equal(0, await ScalarAsync<long>(
            "SELECT count(*) FROM posting_period_events WHERE shop_id = @shop", ("shop", shop.ShopId)));
        Assert.Equal(0, await ScalarAsync<long>(
            "SELECT count(*) FROM operation_receipts WHERE shop_id = @shop", ("shop", shop.ShopId)));
    }

    [Fact]
    public async Task Fence_RequiresAnOpenTransaction()
    {
        using var shop = await OpenShopAsync();
        await using var scope = _factory.Services.CreateAsyncScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
            .GetRequiredService<IPostingPeriodPolicy>()
            .AuthorizeForWriteAsync(shop.ShopId, new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public async Task MissingPeriod_CannotBypassAuthorization()
    {
        using var shop = await OpenShopAsync();
        var month = ClosedMonth();
        Assert.Equal(0, await PeriodRowsAsync(shop));

        await using var posting = await PostingTx.BeginAsync(_factory.Services, shop.ShopId);
        var lease = await posting.AuthorizeAsync(shop.ShopId, month.AddDays(14));
        Assert.False(lease.IsError);
        Assert.Equal(month, lease.Value.StartDate);

        // Nothing existed before the fence; it created the row and holds it.
        var closing = Task.Run(() => SendAsync(shop, HttpMethod.Post, $"/api/posting-periods/{MonthKey(month)}/close", Transition("books final", "missing-row")));
        await WaitUntilBlockedOnPeriodAsync(Waiter.Close);
        Assert.False(closing.IsCompleted);

        await posting.CommitAsync();
        using var response = await closing.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, await PeriodRowsAsync(shop));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT state FROM posting_periods WHERE shop_id = @shop", ("shop", shop.ShopId)));
    }

    [Fact]
    public async Task PostRacesPeriodClose_CommittedBeforeCloseOrRejected()
    {
        // The fence is not wired into the sale workflow yet, so the posting side is a direct
        // transaction that takes the fence and commits a customer ledger entry on the business date.
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var month = ClosedMonth();
        var postingDate = month.AddDays(14);

        // Schedule A: the posting holds the fence, the close queues behind it, the posting commits first.
        await using (var posting = await PostingTx.BeginAsync(_factory.Services, shop.ShopId))
        {
            Assert.False((await posting.AuthorizeAsync(shop.ShopId, postingDate)).IsError);
            var closing = Task.Run(() => SendAsync(shop, HttpMethod.Post, $"/api/posting-periods/{MonthKey(month)}/close", Transition("books final", "race-a")));
            await WaitUntilBlockedOnPeriodAsync(Waiter.Close);
            Assert.False(closing.IsCompleted);

            await posting.AddLedgerAsync(shop, customerId, postingDate);
            await posting.CommitAsync();
            using var response = await closing.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, await LedgerRowsAsync(shop, customerId));
        Assert.Equal(postingDate, await ScalarAsync<DateOnly>(
            "SELECT entry_date FROM customer_ledger_entries WHERE shop_id = @shop AND customer_id = @customer",
            ("shop", shop.ShopId), ("customer", customerId)));
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT state FROM posting_periods WHERE shop_id = @shop AND start_date = @start", ("shop", shop.ShopId), ("start", month)));

        // Schedule B: the close holds the period row; the posting waits, then is refused and writes nothing.
        using (var reopened = await SendAsync(shop, HttpMethod.Post, $"/api/posting-periods/{MonthKey(month)}/reopen", Transition("one more", "race-reopen")))
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        var receiptsBefore = await ScalarAsync<long>("SELECT count(*) FROM operation_receipts WHERE shop_id = @shop", ("shop", shop.ShopId));

        await using var closeTx = await PostingTx.BeginAsync(_factory.Services, shop.ShopId);
        await closeTx.CloseAsync(shop, month);

        var refusedPosting = Task.Run(async () =>
        {
            await using var posting = await PostingTx.BeginAsync(_factory.Services, shop.ShopId);
            var lease = await posting.AuthorizeAsync(shop.ShopId, postingDate);
            if (lease.IsError)
            {
                await posting.RollbackAsync();
                return lease;
            }

            await posting.AddLedgerAsync(shop, customerId, postingDate);
            await posting.CommitAsync();
            return lease;
        });
        await WaitUntilBlockedOnPeriodAsync(Waiter.Posting);
        Assert.False(refusedPosting.IsCompleted);

        await closeTx.CommitAsync();
        var refused = await refusedPosting.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(refused.IsError);
        Assert.Equal(ErrorType.Conflict, refused.FirstError.Type);
        Assert.Equal("PostingPeriod.Closed", refused.FirstError.Code);

        Assert.Equal(1, await LedgerRowsAsync(shop, customerId));
        Assert.Equal(receiptsBefore, await ScalarAsync<long>("SELECT count(*) FROM operation_receipts WHERE shop_id = @shop", ("shop", shop.ShopId)));
        Assert.Equal(3, await ScalarAsync<long>(
            "SELECT count(*) FROM posting_period_events WHERE shop_id = @shop", ("shop", shop.ShopId)));
    }

    [Fact]
    public async Task OnlineSale_IntoAClosedMonth_IsRefusedBeforeAnyNumberOrLock()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);
        Clock.Set(Utc(2026, 6, 15, 6, 0));
        await SellAsync(shop, stock);
        Clock.Set(Utc(2026, 7, 2, 6, 0));
        using (var closed = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition("books final", "sale-close")))
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        Clock.Set(Utc(2026, 6, 20, 6, 0));
        var salesBefore = await ScalarAsync<long>("SELECT count(*) FROM sales WHERE shop_id = @shop", ("shop", shop.ShopId));
        var sequenceBefore = await ScalarAsync<long>("SELECT coalesce(sum(next_number), 0) FROM invoice_sequences WHERE shop_id = @shop", ("shop", shop.ShopId));
        using var refused = await SendAsync(shop, HttpMethod.Post, "/api/sales", SaleBody(stock));
        var body = await refused.Content.ReadAsStringAsync();
        Assert.True(refused.StatusCode == HttpStatusCode.Conflict, body);
        Assert.Contains("PostingPeriod.Closed", body, StringComparison.Ordinal);

        Assert.Equal(salesBefore, await ScalarAsync<long>("SELECT count(*) FROM sales WHERE shop_id = @shop", ("shop", shop.ShopId)));
        Assert.Equal(sequenceBefore, await ScalarAsync<long>("SELECT coalesce(sum(next_number), 0) FROM invoice_sequences WHERE shop_id = @shop", ("shop", shop.ShopId)));
    }

    [Fact]
    public async Task VoidingAJuneAdjustmentInJuly_IsRefused_WhenJuneIsClosed()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);
        Clock.Set(Utc(2026, 6, 20, 6, 0));
        Guid adjustmentId;
        using (var adjust = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{stock.BatchId}/adjust",
            new { direction = (int)InventoryAdjustmentDirection.Decrease, reason = (int)InventoryAdjustmentReason.Damaged, quantity = 1m, performedAt = (DateTimeOffset?)null, notes = "June wastage" }))
        {
            var body = await adjust.Content.ReadAsStringAsync();
            Assert.True(adjust.StatusCode == HttpStatusCode.OK, body);
            adjustmentId = JsonDocument.Parse(body).RootElement.GetProperty("adjustmentId").GetGuid();
        }

        Clock.Set(Utc(2026, 7, 2, 6, 0));
        using (var closed = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition("books final", "adj-void-close")))
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        var junePnl = await ProfitLossBodyAsync(shop, June1, June30);
        var quantityBefore = await ScalarAsync<decimal>("SELECT quantity FROM inventory_batches WHERE id = @id", ("id", stock.BatchId));

        using (var voided = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/adjustments/{adjustmentId}/void", new { reason = "Entered twice" }))
        {
            var body = await voided.Content.ReadAsStringAsync();
            Assert.True(voided.StatusCode == HttpStatusCode.Conflict, body);
            Assert.Contains("PostingPeriod.Closed", body, StringComparison.Ordinal);
        }

        Assert.False(await QueryAsync(db => db.InventoryAdjustments.AsNoTracking().Where(a => a.Id == adjustmentId).Select(a => a.IsVoided).SingleAsync()));
        Assert.Equal(quantityBefore, await ScalarAsync<decimal>("SELECT quantity FROM inventory_batches WHERE id = @id", ("id", stock.BatchId)));
        Assert.Equal(junePnl, await ProfitLossBodyAsync(shop, June1, June30));
    }

    [Fact]
    public async Task ClosedPeriod_BlocksBackdatedPost_AllowsLinkedCurrentCorrection()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);
        var customerId = await AddCustomerAsync(shop);
        Clock.Set(Utc(2026, 6, 15, 6, 0));
        var invoice = await SellAsync(shop, stock, customerId, paid: 18m);
        Clock.Set(Utc(2026, 7, 2, 6, 0));
        using (var closed = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition("books final", "linked-close")))
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        var juneSales = await JuneSalesDatasetAsync(shop);
        var junePnl = await ProfitLossBodyAsync(shop, June1, June30);
        var expensesBefore = await ScalarAsync<long>("SELECT count(*) FROM expenses WHERE shop_id = @shop", ("shop", shop.ShopId));
        var receiptsBefore = await ScalarAsync<long>("SELECT count(*) FROM payment_receipts WHERE shop_id = @shop", ("shop", shop.ShopId));

        // A backdated expense and a backdated customer payment are refused, with nothing written.
        using (var expense = await SendAsync(shop, HttpMethod.Post, "/api/expenses",
            new { categoryName = "Rent", amount = 500m, paidTo = "Landlord", description = (string?)null, expenseDate = "2026-06-20" }))
        {
            var body = await expense.Content.ReadAsStringAsync();
            Assert.True(expense.StatusCode == HttpStatusCode.Conflict, body);
            Assert.Contains("PostingPeriod.Closed", body, StringComparison.Ordinal);
        }

        using (var payment = await SendAsync(shop, HttpMethod.Post, $"/api/customers/{customerId}/payments",
            new { amount = 50m, paymentDate = "2026-06-25", notes = (string?)null, operationKey = "june-backdated" }))
        {
            var body = await payment.Content.ReadAsStringAsync();
            Assert.True(payment.StatusCode == HttpStatusCode.Conflict, body);
            Assert.Contains("PostingPeriod.Closed", body, StringComparison.Ordinal);
        }

        using (var unkeyed = await SendAsync(shop, HttpMethod.Post, $"/api/customers/{customerId}/payments",
            new { amount = 50m, paymentDate = "2026-06-25", notes = (string?)null }))
            Assert.Equal(HttpStatusCode.Conflict, unkeyed.StatusCode);

        Assert.Equal(expensesBefore, await ScalarAsync<long>("SELECT count(*) FROM expenses WHERE shop_id = @shop", ("shop", shop.ShopId)));
        Assert.Equal(receiptsBefore, await ScalarAsync<long>("SELECT count(*) FROM payment_receipts WHERE shop_id = @shop", ("shop", shop.ShopId)));

        // The same payment dated in the open month posts, and so does the return of the June sale today.
        using (var current = await SendAsync(shop, HttpMethod.Post, $"/api/customers/{customerId}/payments",
            new { amount = 50m, paymentDate = "2026-07-02", notes = (string?)null, operationKey = "july-current" }))
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);

        var returnId = await ReturnAsync(shop, invoice);
        var saleReturn = await QueryAsync(db => db.SaleReturns.AsNoTracking().Where(r => r.Id == returnId)
            .Select(r => new { r.BusinessDate, r.SaleId }).SingleAsync());
        Assert.Equal(July2, saleReturn.BusinessDate);
        Assert.Equal(invoice, await QueryAsync(db => db.Sales.AsNoTracking().Where(x => x.Id == saleReturn.SaleId).Select(x => x.InvoiceNumber).SingleAsync()));

        // June's books did not move.
        Assert.Equal(juneSales, await JuneSalesDatasetAsync(shop));
        Assert.Equal(junePnl, await ProfitLossBodyAsync(shop, June1, June30));
    }

    [Fact]
    public async Task ExpenseCorrection_FencesBothMonths_InOrder()
    {
        using var shop = await OpenShopAsync();
        Clock.Set(Utc(2026, 7, 2, 6, 0));

        async Task<Guid> RecordAsync(string date)
        {
            using var response = await SendAsync(shop, HttpMethod.Post, "/api/expenses",
                new { categoryName = "Rent", amount = 500m, paidTo = "Landlord", description = (string?)null, expenseDate = date });
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.Created, body);
            return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
        }

        Task<HttpResponseMessage> CorrectAsync(Guid id, string date) =>
            SendAsync(shop, HttpMethod.Post, $"/api/expenses/{id}/correct",
                new { categoryName = "Rent", amount = 600m, paidTo = "Landlord", description = (string?)null, expenseDate = date });

        // Both months open: a June expense corrected into July succeeds.
        var moved = await RecordAsync("2026-06-20");
        using (var ok = await CorrectAsync(moved, "2026-07-02"))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(await ScalarAsync<bool>("SELECT is_voided FROM expenses WHERE id = @id", ("id", moved)));

        // The original sits in a closed month: the correction is refused whatever its new date.
        var stuck = await RecordAsync("2026-06-21");
        using (var closed = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition("books final", "expense-close")))
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using (var refused = await CorrectAsync(stuck, "2026-07-02"))
        {
            var body = await refused.Content.ReadAsStringAsync();
            Assert.True(refused.StatusCode == HttpStatusCode.Conflict, body);
            Assert.Contains("PostingPeriod.Closed", body, StringComparison.Ordinal);
        }

        Assert.False(await ScalarAsync<bool>("SELECT is_voided FROM expenses WHERE id = @id", ("id", stuck)));

        // A new date in the closed month is refused too, for an original in an open month.
        var open = await RecordAsync("2026-07-01");
        using (var intoClosed = await CorrectAsync(open, "2026-06-30"))
            Assert.Equal(HttpStatusCode.Conflict, intoClosed.StatusCode);
        Assert.False(await ScalarAsync<bool>("SELECT is_voided FROM expenses WHERE id = @id", ("id", open)));
    }

    [Fact]
    public async Task StockPostings_IntoAClosedMonth_AreRefused_PerRowForBulkIntake()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);
        Clock.Set(Utc(2026, 6, 15, 6, 0));
        var invoice = await SellAsync(shop, stock);
        Clock.Set(Utc(2026, 7, 2, 6, 0));
        using (var closed = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition("books final", "stock-close")))
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        object Row(string id, string performedAt) => new
        {
            clientRowId = id,
            itemName = $"Closed Item {id}",
            barcode = $"CL-{Guid.NewGuid():N}",
            itemDescription = (string?)null,
            uom = "pcs",
            batchNumber = $"CB-{id}",
            quantity = 5m,
            totalPurchaseCost = 400m,
            mrp = 120m,
            salesPrice = 100m,
            taxRatePercent = 18m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt,
        };

        // Bulk intake fences per row: the June-dated row is held back, the July row commits.
        using (var bulk = await SendAsync(shop, HttpMethod.Post, "/api/inventory/inbound/batch",
            new { operationKey = "closed-bulk", items = new[] { Row("june", "2026-06-20T06:00:00Z"), Row("july", "2026-07-02T06:00:00Z") } }))
        {
            var body = await bulk.Content.ReadAsStringAsync();
            Assert.True(bulk.IsSuccessStatusCode, body); // 207 when only some rows commit
            var json = JsonDocument.Parse(body).RootElement;
            Assert.Equal(1, json.GetProperty("successCount").GetInt32());
            Assert.Equal("july", json.GetProperty("succeeded")[0].GetProperty("clientRowId").GetString());
            Assert.Contains("PostingPeriod.Closed", json.GetProperty("failed").GetRawText(), StringComparison.Ordinal);
        }

        using (var single = await SendAsync(shop, HttpMethod.Post, "/api/inventory/inbound", Row("single", "2026-06-20T06:00:00Z")))
            Assert.Equal(HttpStatusCode.Conflict, single.StatusCode);

        Clock.Set(Utc(2026, 6, 20, 6, 0));
        using (var adjust = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{stock.BatchId}/adjust",
            new { direction = (int)InventoryAdjustmentDirection.Decrease, reason = (int)InventoryAdjustmentReason.Damaged, quantity = 1m, performedAt = (DateTimeOffset?)null, notes = "x" }))
            Assert.Equal(HttpStatusCode.Conflict, adjust.StatusCode);
        using (var voided = await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{stock.BatchId}/void"))
        {
            var body=await voided.Content.ReadAsStringAsync();
            Assert.True(voided.StatusCode==HttpStatusCode.Conflict,$"Expected closed-period409, got {voided.StatusCode}: {body}");
            Assert.Contains("PostingPeriod.Closed",body,StringComparison.Ordinal);
        }
        using (var returned = await SendAsync(shop, HttpMethod.Post, $"/api/sales/{await SaleIdAsync(invoice)}/returns", new
        {
            payoutDestination = (int)ReturnPayoutDestination.Refund,
            items = new[] { new { saleItemId = await SaleLineIdAsync(invoice), quantity = 1m, lineType = (int)SaleLineType.Goods, condition = (int)SaleReturnCondition.Restockable } },
        }))
        {
            var body = await returned.Content.ReadAsStringAsync();
            Assert.True(returned.StatusCode == HttpStatusCode.Conflict, body);
            Assert.Contains("PostingPeriod.Closed", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LateOfflineInvoice_RequiresExplicitResolution()
    {
        using var shop = await OpenShopAsync();
        var stock = await StockAsync(shop);

        // The till reserves its range on 28 June (valid to 5 July) and issues the invoice at 23:00 IST on 30 June.
        Clock.Set(Utc(2026, 6, 28, 6, 0));
        using var reserved = await SendAsync(shop, HttpMethod.Post, "/api/sales/invoice-leases/reserve", new { deviceId = "till-1", blockSize = 25 });
        reserved.EnsureSuccessStatusCode();
        var lease = await reserved.Content.ReadFromJsonAsync<JsonElement>();
        var invoice = $"{lease.GetProperty("prefix").GetString()}{lease.GetProperty("rangeStart").GetInt32().ToString($"D{lease.GetProperty("numberPadding").GetInt32()}", CultureInfo.InvariantCulture)}";
        var soldAt = Utc(2026, 6, 30, 17, 30);
        var payload = OfflinePayload("late-1", invoice, stock, soldAt);

        // June closes on 2 July, before the device syncs (still inside the lease and the skew rules).
        Clock.Set(Utc(2026, 7, 2, 6, 0));
        using (var closed = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/close", Transition("books final", "late-close")))
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        var held = await OfflineSyncAsync(shop, payload);
        Assert.Equal("NeedsReview", held.GetProperty("status").GetString());
        Assert.Equal("PostingPeriod.Closed", held.GetProperty("errors")[0].GetProperty("code").GetString());
        var caseId = held.GetProperty("reviewCaseId").GetGuid();
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM sales WHERE shop_id = @shop", ("shop", shop.ShopId)));
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM stock_transactions WHERE shop_id = @shop AND transaction_type = 'Out'", ("shop", shop.ShopId)));

        using var detailResponse = await SendAsync(shop, HttpMethod.Get, $"/api/offline-reconciliation/{caseId}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ClosedPeriodReview", detail.GetProperty("issueType").GetString());
        Assert.Contains("RetryHistoricalLease", detail.GetProperty("availableActions").EnumerateArray().Select(a => a.GetString()));
        var version = detail.GetProperty("version").GetUInt32();

        // Replaying while the month is closed is refused and the case stays open.
        using (var blocked = await ResolveRetryAsync(shop, caseId, version, "retry-closed"))
        {
            var blockedBody = await blocked.Content.ReadAsStringAsync();
            Assert.True(blocked.StatusCode == HttpStatusCode.Conflict, blockedBody);
            Assert.Contains("PostingPeriod.Closed", blockedBody, StringComparison.Ordinal);
        }

        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM sales WHERE shop_id = @shop", ("shop", shop.ShopId)));
        using (var again = await SendAsync(shop, HttpMethod.Get, $"/api/offline-reconciliation/{caseId}"))
        {
            var current = await again.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Open", current.GetProperty("status").GetString());
            version = current.GetProperty("version").GetUInt32();
        }

        // An owner reopens June; the same case now posts the original invoice on its original date.
        using (var reopened = await SendAsync(shop, HttpMethod.Post, "/api/posting-periods/2026-06/reopen", Transition("late invoice", "late-reopen")))
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);

        // A device resend of the same envelope does not post past the open case.
        var resent = await OfflineSyncAsync(shop, payload);
        Assert.Equal("NeedsReview", resent.GetProperty("status").GetString());
        Assert.Equal(caseId, resent.GetProperty("reviewCaseId").GetGuid());
        Assert.Equal(0, await ScalarAsync<long>("SELECT count(*) FROM sales WHERE shop_id = @shop", ("shop", shop.ShopId)));
        using (var retried = await ResolveRetryAsync(shop, caseId, version, "retry-open"))
        {
            var retriedBody = await retried.Content.ReadAsStringAsync();
            Assert.True(retried.StatusCode == HttpStatusCode.OK, retriedBody);
        }

        Assert.Equal(invoice, await ScalarAsync<string>("SELECT invoice_number FROM sales WHERE shop_id = @shop", ("shop", shop.ShopId)));
        Assert.Equal(new DateOnly(2026, 6, 30), await ScalarAsync<DateOnly>("SELECT business_date FROM sales WHERE shop_id = @shop", ("shop", shop.ShopId)));
        Assert.Equal(soldAt, await ScalarAsync<DateTimeOffset>("SELECT sold_at FROM sales WHERE shop_id = @shop", ("shop", shop.ShopId)));
    }

    [Fact]
    public async Task Fence_AndTransitionLock_PutBackTheCallersLockBudget()
    {
        using var shop = await OpenShopAsync();
        var month = ClosedMonth();

        await using (var withBudget = await PostingTx.BeginAsync(_factory.Services, shop.ShopId))
        {
            await withBudget.SetLockTimeoutAsync("7s");
            Assert.False((await withBudget.AuthorizeAsync(shop.ShopId, month.AddDays(3))).IsError);
            Assert.Equal("7s", await withBudget.LockTimeoutAsync());
            await withBudget.RollbackAsync();
        }

        await using (var withDefault = await PostingTx.BeginAsync(_factory.Services, shop.ShopId))
        {
            var before = await withDefault.LockTimeoutAsync();
            Assert.False((await withDefault.AuthorizeAsync(shop.ShopId, month.AddDays(3))).IsError);
            Assert.Equal(before, await withDefault.LockTimeoutAsync());
            await withDefault.RollbackAsync();
        }

        await using (var transition = await PostingTx.BeginAsync(_factory.Services, shop.ShopId))
        {
            await transition.SetLockTimeoutAsync("7s");
            await transition.LockForTransitionAsync(shop.ShopId, month);
            Assert.Equal("7s", await transition.LockTimeoutAsync());
            await transition.RollbackAsync();
        }
    }

    [Fact]
    public async Task Fence_WaitingOnAClose_ReturnsBusyAfterTheBudget()
    {
        using var shop = await OpenShopAsync();
        var month = ClosedMonth();
        await using (var warm = await PostingTx.BeginAsync(_factory.Services, shop.ShopId))
        {
            Assert.False((await warm.AuthorizeAsync(shop.ShopId, month.AddDays(3))).IsError);
            await warm.CommitAsync();
        }

        await using var closeTx = await PostingTx.BeginAsync(_factory.Services, shop.ShopId);
        await closeTx.CloseAsync(shop, month);

        await using var posting = await PostingTx.BeginAsync(_factory.Services, shop.ShopId);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await posting.AuthorizeAsync(shop.ShopId, month.AddDays(3));
        watch.Stop();

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("PostingPeriod.Busy", result.FirstError.Code);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15));
        await posting.RollbackAsync();
        await closeTx.RollbackAsync();
    }

    [Fact]
    public async Task Close_WaitingOnAPosting_ReturnsBusyAfterTheBudget()
    {
        using var shop = await OpenShopAsync();
        var month = ClosedMonth();

        await using var posting = await PostingTx.BeginAsync(_factory.Services, shop.ShopId);
        Assert.False((await posting.AuthorizeAsync(shop.ShopId, month.AddDays(3))).IsError);

        using var response = await SendAsync(shop, HttpMethod.Post, $"/api/posting-periods/{MonthKey(month)}/close", Transition("books final", "busy-close"))
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("PostingPeriod.Busy", body.RootElement.GetRawText(), StringComparison.Ordinal);
        await posting.RollbackAsync();
    }

    [Fact]
    public async Task PostingAfterClosedCheck_CannotCommitLate()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var month = ClosedMonth();
        var postingDate = month.AddDays(9);

        // The month's row is committed first, so the probe below can see and try to lock it.
        await using (var warm = await PostingTx.BeginAsync(_factory.Services, shop.ShopId))
        {
            Assert.False((await warm.AuthorizeAsync(shop.ShopId, postingDate)).IsError);
            await warm.CommitAsync();
        }

        Task<HttpResponseMessage> closing;
        DateTimeOffset beforeCommit;
        await using (var posting = await PostingTx.BeginAsync(_factory.Services, shop.ShopId))
        {
            // Authorized against an open month, then slow: it has not written or committed yet.
            Assert.False((await posting.AuthorizeAsync(shop.ShopId, postingDate)).IsError);
            await posting.AddLedgerAsync(shop, customerId, postingDate);

            closing = Task.Run(() => SendAsync(shop, HttpMethod.Post, $"/api/posting-periods/{MonthKey(month)}/close", Transition("books final", "late-a")));
            await WaitUntilBlockedOnPeriodAsync(Waiter.Close);
            Assert.False(closing.IsCompleted);

            // Direct proof the close cannot take the row: a second locker with a short lock_timeout fails.
            await using var probe = new NpgsqlConnection(_factory.MigratorConnectionString);
            await probe.OpenAsync();
            await using var probeTx = await probe.BeginTransactionAsync();
            await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '300ms'", probe, probeTx))
                await timeout.ExecuteNonQueryAsync();
            await using var lockRow = new NpgsqlCommand(
                "SELECT id FROM posting_periods WHERE shop_id = @shop AND start_date = @start FOR UPDATE", probe, probeTx);
            lockRow.Parameters.AddWithValue("shop", shop.ShopId);
            lockRow.Parameters.AddWithValue("start", month);
            var timedOut = await Assert.ThrowsAsync<PostgresException>(() => lockRow.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, timedOut.SqlState);

            beforeCommit = DateTimeOffset.UtcNow;
            await posting.CommitAsync();
        }

        using var response = await closing.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var closedAt = await ScalarAsync<DateTimeOffset>(
            "SELECT occurred_at FROM posting_period_events WHERE shop_id = @shop AND action = 1", ("shop", shop.ShopId));
        Assert.True(closedAt >= beforeCommit, $"close recorded {closedAt:O} before the posting committed ({beforeCommit:O})");

        // The posting survived, and no row of the month was created after the close was recorded.
        Assert.Equal(1, await LedgerRowsAsync(shop, customerId));
        Assert.Equal(0, await ScalarAsync<long>(
            """
            SELECT count(*) FROM customer_ledger_entries l
            JOIN posting_periods p ON p.shop_id = l.shop_id AND l.entry_date BETWEEN p.start_date AND p.end_date
            JOIN posting_period_events e ON e.posting_period_id = p.id AND e.action = 1
            WHERE l.shop_id = @shop AND l.created_at > e.occurred_at
            """,
            ("shop", shop.ShopId)));
    }

    private static object SaleBody(Stock stock) => new
    {
        idempotencyKey = $"sale-{Guid.NewGuid():N}",
        customerName = "Walk-in Customer",
        customerPhone = "+919876543210",
        paymentMethod = (int)PaymentMethod.Cash,
        paidAmount = 118m,
        dueAmount = 0m,
        items = new[]
        {
            new
            {
                barcode = stock.Barcode,
                batchNumber = "B-001",
                itemName = stock.ItemName,
                quantity = 1m,
                costPrice = 80m,
                salesPrice = 100m,
                mrp = 120m,
                taxRatePercent = 18m,
                isPriceIncludingTax = false,
                inventoryBatchId = stock.BatchId,
            },
        },
    };

    private static object OfflinePayload(string clientSaleId, string invoiceNumber, Stock stock, DateTimeOffset soldAt) => new
    {
        clientSaleId,
        invoiceNumber,
        soldAt,
        paymentMethod = (int)PaymentMethod.Cash,
        paidAmount = 118m,
        dueAmount = 0m,
        subtotalBeforeDiscount = 100m,
        totalBeforeDiscount = 118m,
        totalDiscountAmount = 0m,
        totalTaxAmount = 18m,
        totalAmount = 118m,
        saleDiscountOverrideType = (int)InstantDiscountType.None,
        saleDiscountOverrideValue = 0m,
        items = new[]
        {
            new
            {
                barcode = stock.Barcode,
                batchNumber = "B-001",
                itemName = stock.ItemName,
                quantity = 1m,
                costPrice = 80m,
                salesPrice = 100m,
                mrp = 120m,
                taxRatePercent = 18m,
                isPriceIncludingTax = false,
                inventoryBatchId = stock.BatchId,
                preTaxAmountBeforeDiscount = 100m,
                itemDiscountAmount = 0m,
                saleDiscountAmount = 0m,
                taxableAmount = 100m,
                taxAmount = 18m,
                totalAmount = 118m,
                itemDiscountOverrideType = (int)InstantDiscountType.None,
                itemDiscountOverrideValue = 0m,
            },
        },
    };

    private async Task<JsonElement> OfflineSyncAsync(Shop shop, object sale)
    {
        await LegacyOfflineExpiryHistory.SeedAsync(_factory, shop.Token);
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/sales/offline-sync", new { deviceId = "till-1", sales = new[] { sale } });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);
        return JsonDocument.Parse(content).RootElement.GetProperty("results")[0].Clone();
    }

    private static Task<HttpResponseMessage> ResolveRetryAsync(Shop shop, Guid caseId, uint version, string operationId) =>
        SendAsync(shop, HttpMethod.Post, $"/api/offline-reconciliation/{caseId}/resolve", new
        {
            operationId,
            expectedVersion = version,
            action = "RetryHistoricalLease",
            reason = "Owner reopened the month for this invoice",
        });

    private static DateOnly ClosedMonth() =>
        new DateOnly(BusinessDates.Today.Year, BusinessDates.Today.Month, 1).AddMonths(-2);

    private static string MonthKey(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static object Transition(string reason, string operationKey) => new { reason, operationKey };

    private static async Task<Shop> SwitchTokenAsync(Shop shop)
    {
        using var login = await shop.Client.PostAsJsonAsync("/api/auth/login/email", new { email = shop.Email, password = "Pass123!Aa" });
        var loginBody = await login.Content.ReadAsStringAsync();
        Assert.True(login.StatusCode == HttpStatusCode.OK, loginBody);
        var signedIn = shop with { Token = JsonDocument.Parse(loginBody).RootElement.GetProperty("accessToken").GetString()! };

        using var response = await SendAsync(signedIn, HttpMethod.Post, "/api/shops/switch", new { shopId = shop.ShopId });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {content}");
        return shop with { Token = JsonDocument.Parse(content).RootElement.GetProperty("accessToken").GetString()! };
    }

    private async Task SetRoleAsync(Shop shop, string role)
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE shop_memberships SET role = @role WHERE shop_id = @shop AND user_id = @user", connection);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("shop", shop.ShopId);
        command.Parameters.AddWithValue("user", shop.UserId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    /// <summary>Reads as the database owner, so row security does not hide what the test asserts on.</summary>
    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        var scalar = (await command.ExecuteScalarAsync())!;
        return scalar is DateTime instant && typeof(T) == typeof(DateTimeOffset)
            ? (T)(object)new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc))
            : (T)scalar;
    }

    private Task<long> PeriodRowsAsync(Shop shop) =>
        ScalarAsync<long>("SELECT count(*) FROM posting_periods WHERE shop_id = @shop", ("shop", shop.ShopId));

    private Task<long> LedgerRowsAsync(Shop shop, Guid customerId) =>
        ScalarAsync<long>(
            "SELECT count(*) FROM customer_ledger_entries WHERE shop_id = @shop AND customer_id = @customer",
            ("shop", shop.ShopId), ("customer", customerId));

    private enum Waiter
    {
        /// <summary>A close or reopen: queued on the row's FOR UPDATE, or on the first-touch insert.</summary>
        Close,

        /// <summary>A posting fence: queued on the row's FOR SHARE, or on the first-touch insert.</summary>
        Posting,
    }

    /// <summary>
    /// Waits until a session is parked on a lock in the specific statement the given waiter runs, so an
    /// unrelated waiter (another fence caller, a sale) can never satisfy it.
    /// </summary>
    private async Task WaitUntilBlockedOnPeriodAsync(Waiter waiter)
    {
        var lockStatement = waiter == Waiter.Close ? "%FROM posting_periods%FOR UPDATE%" : "%FROM posting_periods%FOR SHARE%";
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var waiting = await ScalarAsync<long>(
                """
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND pid <> pg_backend_pid()
                  AND wait_event_type = 'Lock'
                  AND (query ILIKE @lockStatement OR query ILIKE '%INSERT INTO posting_periods%')
                """,
                ("lockStatement", lockStatement));
            if (waiting > 0)
                return;

            await Task.Delay(50);
        }

        Assert.Fail($"No {waiter} session ever waited on a posting period lock.");
    }

    /// <summary>
    /// One independent posting-style transaction: its own scope, context and connection, with the
    /// shop's row security context set the way the tenant write scope does.
    /// </summary>
    private sealed class PostingTx : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly ApplicationDbContext _db;
        private readonly IDbContextTransaction _transaction;
        private bool _finished;

        private PostingTx(AsyncServiceScope scope, ApplicationDbContext db, IDbContextTransaction transaction)
        {
            _scope = scope;
            _db = db;
            _transaction = transaction;
        }

        public static async Task<PostingTx> BeginAsync(IServiceProvider services, Guid shopId)
        {
            var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<ITenantWriteScope>().EnterAsync(shopId);
            return new PostingTx(scope, db, transaction);
        }

        public Task<ErrorOr<PostingPeriodLease>> AuthorizeAsync(Guid shopId, DateOnly businessDate) =>
            _scope.ServiceProvider.GetRequiredService<IPostingPeriodPolicy>().AuthorizeForWriteAsync(shopId, businessDate);

        public async Task<string> LockTimeoutAsync() =>
            (await _db.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").ToListAsync()).Single();

        public async Task SetLockTimeoutAsync(string value) =>
            await _db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {value}, true)");

        public async Task LockForTransitionAsync(Guid shopId, DateOnly month)
        {
            var (start, _) = PostingPeriod.MonthOf(month);
            var locked = await _scope.ServiceProvider.GetRequiredService<IPostingPeriodRepository>().LockForTransitionAsync(shopId, start);
            Assert.NotEqual(RowLockOutcome.TimedOut, locked.Outcome);
        }

        public async Task AddLedgerAsync(Shop shop, Guid customerId, DateOnly date)
        {
            var entry = CustomerLedgerEntry.Create(
                shop.ShopId, customerId, saleId: null, CustomerLedgerEntryType.PaymentReceived, 10m, date, "stand-in posting", shop.UserId);
            Assert.False(entry.IsError);
            _db.CustomerLedgerEntries.Add(entry.Value);
            await _db.SaveChangesAsync();
        }

        /// <summary>A close that has done everything but commit: it holds the period row FOR UPDATE.</summary>
        public async Task CloseAsync(Shop shop, DateOnly month)
        {
            var periods = _scope.ServiceProvider.GetRequiredService<IPostingPeriodRepository>();
            var (start, end) = PostingPeriod.MonthOf(month);
            await periods.EnsureExistsAsync(shop.ShopId, start, end);
            var locked = await periods.LockForTransitionAsync(shop.ShopId, start);
            var period = locked.Period;
            Assert.NotNull(period);
            var closed = period.Close(shop.UserId, DateTimeOffset.UtcNow, BusinessDates.Today, "held close");
            Assert.False(closed.IsError);
            await periods.AddEventAsync(closed.Value);
            await _db.SaveChangesAsync();
        }

        public async Task CommitAsync()
        {
            await _transaction.CommitAsync();
            _finished = true;
        }

        public async Task RollbackAsync()
        {
            await _transaction.RollbackAsync();
            _finished = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_finished)
                await _transaction.RollbackAsync();

            await _transaction.DisposeAsync();
            await _scope.DisposeAsync();
        }
    }

    // ── Setup ───────────────────────────────────────────────────────────────────

    private static readonly DateOnly Oct20 = new(2026, 10, 20);
    private static readonly DateOnly Oct21 = new(2026, 10, 21);
    private static readonly DateOnly June1 = new(2026, 6, 1);
    private static readonly DateOnly June30 = new(2026, 6, 30);
    private static readonly DateOnly July1 = new(2026, 7, 1);
    private static readonly DateOnly July2 = new(2026, 7, 2);
    private static readonly DateOnly July31 = new(2026, 7, 31);
    private static readonly DateOnly Aug1 = new(2026, 8, 1);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(year, month, day, hour, minute, second, TimeSpan.Zero);

    private sealed class SettableClock : TimeProvider
    {
        private DateTimeOffset? _at;

        public void Set(DateTimeOffset at) => _at = at;

        public override DateTimeOffset GetUtcNow() => _at ?? System.GetUtcNow();
    }

    private sealed record Shop(HttpClient Client, string Token, Guid ShopId, Guid UserId, string Email) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed record Stock(string Barcode, string ItemName, Guid BatchId);

    private sealed record SaleRow(string InvoiceNumber);

    private sealed record AdjustmentRow(string Entry, string Invoice);

    private sealed record TallyVoucher(string Number, string Type, string Date);

    // Sign-in runs on the real clock; the clock only moves once the token exists.
    private async Task<Shop> OpenShopAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var email = $"business-period-{Guid.NewGuid():N}@test.com";
        var enrolled = await client.EnrollAsync(new
        {
            email,
            password = "Pass123!Aa",
            firstName = "Period",
            lastName = "Owner",
        });
        enrolled.EnsureSuccessStatusCode();
        var enrolledBody = await enrolled.Content.ReadFromJsonAsync<JsonElement>();
        var token = enrolledBody.GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
            gstNumber = TestTaxProfile.NewGstin(),
            taxProfile = TestTaxProfile.Regular,
        });
        using var created = await client.SendAsync(request);
        created.EnsureSuccessStatusCode();
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var shopToken = createdBody.GetProperty("accessToken").GetString()!;
        var claims = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(shopToken).Claims.ToList();
        var shopId = Guid.Parse(claims.Single(c => c.Type == "active_shop_id").Value);
        var userId = Guid.Parse(claims.Single(c => c.Type is "sub" or "nameid" or System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        return new Shop(client, shopToken, shopId, userId, email);
    }

    private static async Task<HttpResponseMessage> SendAsync(Shop shop, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", shop.Token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await shop.Client.SendAsync(request);
    }

    private static async Task<JsonDocument> GetJsonAsync(Shop shop, string path)
    {
        using var response = await SendAsync(shop, HttpMethod.Get, path);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);
        return JsonDocument.Parse(content);
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

    /// <summary>Plenty of one item at 100 + 18% GST, so a sale is Rs 118.</summary>
    private static async Task<Stock> StockAsync(Shop shop)
    {
        var barcode = $"BP-{Guid.NewGuid():N}";
        var itemName = $"Period Item {barcode[3..11]}";
        using var stock = await SendAsync(shop, HttpMethod.Post, "/api/inventory/inbound", new
        {
            itemName,
            barcode,
            itemDescription = (string?)null,
            hsnCode = (string?)null,
            uom = "pcs",
            batchNumber = "B-001",
            quantity = 100m,
            totalPurchaseCost = 8000m,
            mrp = 120m,
            salesPrice = 100m,
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
        var content = await stock.Content.ReadAsStringAsync();
        Assert.True(stock.IsSuccessStatusCode, content);
        var batchId = JsonSerializer.Deserialize<JsonElement>(content).GetProperty("inventoryBatchId").GetGuid();
        return new Stock(barcode, itemName, batchId);
    }

    /// <summary>One Rs 118 sale at the current clock; answers its invoice number.</summary>
    private async Task<string> SellAsync(Shop shop, Stock stock, Guid? customerId = null, decimal paid = 118m)
    {
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId,
            customerName = customerId.HasValue ? "Due Customer" : "Walk-in Customer",
            customerPhone = customerId.HasValue ? "+919812345678" : "+919876543210",
            paymentMethod = paid < 118m ? (int)PaymentMethod.Credit : (int)PaymentMethod.Cash,
            paidAmount = paid,
            dueAmount = 118m - paid,
            items = new[]
            {
                new
                {
                    barcode = stock.Barcode,
                    batchNumber = "B-001",
                    itemName = stock.ItemName,
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = stock.BatchId,
                },
            },
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, content);
        var saleId = JsonSerializer.Deserialize<JsonElement>(content).GetProperty("saleId").GetGuid();
        return await QueryAsync(db => db.Sales.AsNoTracking().Where(s => s.Id == saleId).Select(s => s.InvoiceNumber).SingleAsync());
    }

    private async Task<Guid> ReturnAsync(Shop shop, string invoiceNumber)
    {
        var (saleId, lineId) = await QueryAsync(async db =>
        {
            var sale = await db.Sales.AsNoTracking().Include(s => s.Items).SingleAsync(s => s.InvoiceNumber == invoiceNumber);
            return (sale.Id, sale.Items.Single().Id);
        });
        using var response = await SendAsync(shop, HttpMethod.Post, $"/api/sales/{saleId}/returns", new
        {
            payoutDestination = (int)ReturnPayoutDestination.Refund,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = (string?)null,
            creditNoteReason = (string?)null,
            items = new[]
            {
                new
                {
                    saleItemId = lineId,
                    quantity = 1m,
                    lineType = (int)SaleLineType.Goods,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = (decimal?)null,
                    notes = (string?)null,
                },
            },
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);
        return await QueryAsync(db => db.SaleReturns.AsNoTracking().Where(r => r.SaleId == saleId).Select(r => r.Id).SingleAsync());
    }

    // ── Reads ───────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<SaleRow>> SalesListAsync(Shop shop, DateOnly from, DateOnly to)
    {
        using var body = await GetJsonAsync(shop, $"/api/sales?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&pageSize=100");
        return body.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => new SaleRow(i.GetProperty("invoiceNumber").GetString()!))
            .ToList();
    }

    private static Task<HttpResponseMessage> ExportAsync(Shop shop, string format, DateOnly from, DateOnly to) =>
        SendAsync(shop, HttpMethod.Get, $"/api/exports/sales?format={format}&level=summary&startDate={from:yyyy-MM-dd}&endDate={to:yyyy-MM-dd}");

    private static async Task<IReadOnlyList<TallyVoucher>> TallyVouchersAsync(Shop shop, DateOnly from, DateOnly to)
    {
        using var response = await ExportAsync(shop, "tallyXml", from, to);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);
        return XDocument.Parse(content).Descendants("VOUCHER")
            .Select(v => new TallyVoucher((string)v.Element("VOUCHERNUMBER")!, (string)v.Element("VOUCHERTYPE")!, (string)v.Element("DATE")!))
            .ToList();
    }

    private static async Task<XLWorkbook> WorkbookAsync(Shop shop, DateOnly from, DateOnly to)
    {
        using var response = await ExportAsync(shop, "xlsx", from, to);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
    }

    private static async Task<IReadOnlyList<string>> XlsxSaleNumbersAsync(Shop shop, DateOnly from, DateOnly to)
    {
        using var workbook = await WorkbookAsync(shop, from, to);
        var sheet = workbook.Worksheets.First(w => w.Name != "Return Adjustments");
        var header = sheet.RowsUsed().First(r => r.CellsUsed().Any(c => c.GetString() == "Invoice #"));
        var column = header.CellsUsed().First(c => c.GetString() == "Invoice #").Address.ColumnNumber;
        var numbers = new List<string>();
        for (var row = header.RowNumber() + 1; !sheet.Cell(row, column).IsEmpty(); row++)
            numbers.Add(sheet.Cell(row, column).GetString());
        return numbers.Where(n => n != "Returns on earlier sales").ToList();
    }

    private static async Task<IReadOnlyList<AdjustmentRow>> XlsxAdjustmentRowsAsync(Shop shop, DateOnly from, DateOnly to)
    {
        using var workbook = await WorkbookAsync(shop, from, to);
        var sheet = workbook.Worksheet("Return Adjustments");
        var header = sheet.RowsUsed().First(r => r.Cell(1).GetString() == "Posting Date").RowNumber();
        var rows = new List<AdjustmentRow>();
        for (var row = header + 1; !sheet.Cell(row, 3).IsEmpty(); row++)
            rows.Add(new AdjustmentRow(sheet.Cell(row, 2).GetString(), sheet.Cell(row, 4).GetString()));
        return rows;
    }

    private Task<Guid> SaleIdAsync(string invoice) =>
        QueryAsync(db => db.Sales.AsNoTracking().Where(x => x.InvoiceNumber == invoice).Select(x => x.Id).SingleAsync());

    private Task<Guid> SaleLineIdAsync(string invoice) =>
        QueryAsync(db => db.Sales.AsNoTracking().Where(x => x.InvoiceNumber == invoice).SelectMany(x => x.Items).Select(i => i.Id).SingleAsync());

    private static async Task<string> ProfitLossBodyAsync(Shop shop, DateOnly from, DateOnly to)
    {
        using var response = await SendAsync(shop, HttpMethod.Get, $"/api/sales/profit-loss?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&pageSize=100");
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, content);
        return content;
    }

    /// <summary>Every cell of the June sales export, as text; equal before and after means the same totals.</summary>
    private static async Task<IReadOnlyList<string>> JuneSalesDatasetAsync(Shop shop)
    {
        using var workbook = await WorkbookAsync(shop, June1, June30);
        return workbook.Worksheets
            .SelectMany(w => w.CellsUsed()
                .Where(c => w.Cell(c.Address.RowNumber, 1).GetString() != "Generated:") // the export stamps when it ran
                .Select(c => $"{w.Name}!{c.Address}={c.GetString()}"))
            .ToList();
    }

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
