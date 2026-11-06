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
        Clock.Set(Utc(2026, 7, 10, 6, 0));
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
        Clock.Set(Utc(2026, 7, 10, 6, 0));
        var returnId = await ReturnAsync(shop, invoice);
        Clock.Set(Utc(2026, 8, 5, 6, 0));
        using (var voided = await SendAsync(shop, HttpMethod.Post, $"/api/sales/returns/{returnId}/void", new { reason = "Wrong invoice" }))
            Assert.Equal(HttpStatusCode.NoContent, voided.StatusCode);

        Assert.Equal(["Return"], (await XlsxAdjustmentRowsAsync(shop, July1, July31)).Select(r => r.Entry).ToArray());
        var august = Assert.Single(await XlsxAdjustmentRowsAsync(shop, Aug1, Aug31));
        Assert.Equal(("Void reversal", invoice), (august.Entry, august.Invoice));
        Assert.Empty(await XlsxAdjustmentRowsAsync(shop, June1, June30));
        Assert.Equal([invoice], (await XlsxSaleNumbersAsync(shop, June1, June30)).ToArray());
    }

    // ── Setup ───────────────────────────────────────────────────────────────────

    private static readonly DateOnly Oct20 = new(2026, 10, 20);
    private static readonly DateOnly Oct21 = new(2026, 10, 21);
    private static readonly DateOnly June1 = new(2026, 6, 1);
    private static readonly DateOnly June30 = new(2026, 6, 30);
    private static readonly DateOnly July1 = new(2026, 7, 1);
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

    private sealed record Shop(HttpClient Client, string Token, Guid ShopId, Guid UserId) : IDisposable
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

        var enrolled = await client.EnrollAsync(new
        {
            email = $"business-period-{Guid.NewGuid():N}@test.com",
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
        return new Shop(client, shopToken, shopId, userId);
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

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
