using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Intelibill.Application.Common.Time;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Intelibill.Infrastructure.Options;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SaleCancellationReportingTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory factory = new(fixture, services =>
    {
        services.RemoveAll<IBusinessClock>();
        services.AddSingleton<SettableClock>();
        services.AddSingleton<IBusinessClock>(sp => new Intelibill.Infrastructure.Services.Time.BusinessClock(sp.GetRequiredService<SettableClock>()));
    });
    private sealed class SettableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public Task InitializeAsync() => factory.InitializeAsync();
    public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }
    public void Dispose() { factory.Dispose(); GC.SuppressFinalize(this); }

    private sealed record Seed(HttpClient Client, Guid ShopId, Guid ActorId, Sale Sale, Guid BatchId, Guid CustomerId);
    private async Task<Seed> SeedAsync(decimal paid = 200m, PaymentMethod method = PaymentMethod.Cash, bool split = false, decimal cost = 60m, decimal quantity = 2m)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        var registration = await client.EnrollAsync(new { email = $"cancel-{Guid.NewGuid():N}@test.com", password = "Pass123!Aa", firstName = "Cancel", lastName = "Owner", phoneNumber = $"+91{Random.Shared.NextInt64(1000000000, 9999999999)}" });
        registration.EnsureSuccessStatusCode();
        var token = (await registration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var create = await client.PostAsJsonAsync("/api/shops", new { name = "Cancellation shop", address = "Address", city = "City", state = "Karnataka", pincode = "560001" });
        create.EnsureSuccessStatusCode();
        token = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var actor = Guid.Parse(jwt.Subject);
        var shop = Guid.Parse(jwt.Claims.Single(x => x.Type == "active_shop_id").Value);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var customer = Customer.Create(shop, "Buyer", Guid.NewGuid().ToString("N")[..10], null);
        var item = Item.Create(shop, "Sugar", null, "kg", Guid.NewGuid().ToString("N"), true, actor);
        var batch = InventoryBatch.Create(shop, item.Id, "LOT", 8m, cost, 100m, 100m, 0m, false, null, null, null, actor).Value;
        var stock = Inventory.Create(shop, item.Id, 8m, 0m, 100m, actor).Value;
        var now = factory.Services.GetRequiredService<IBusinessClock>().UtcNow;
        var total = quantity * 100m;
        SaleLineInput Line(decimal q) => new(shop, SaleLineType.Goods, item.Id, batch.Id, null, "Frozen sugar", "SUGAR", q, cost, 100m, 100m, 0m, false, false);
        var sale = Sale.Record(shop, actor, Guid.NewGuid().ToString("N"), "HASH", "A/2627/000001", split ? [Line(quantity / 2m), Line(quantity / 2m)] : [Line(quantity)], customer.Id, "Buyer", null, method, paid, total - paid, now, BusinessDay.InDefaultZone(now)).Value;
        sale.AttachInvoiceDocument(new(2, InvoiceDocumentType.Receipt, now, sale.BusinessDate, 2026, "A", sale.InvoiceNumber,
            new("Frozen seller", "Shop", "Address", "29", "Karnataka", null, TaxRegime.Unregistered), new("Frozen buyer", null, null, null, null, null), "29", "Karnataka", false, false,
            sale.Items.Select((x, i) => new InvoiceDocumentLine(i + 1, "Frozen sugar", null, "KGS", x.Quantity, 100m, 0m, x.TotalAmount, 0m, true, [], x.TotalAmount)).ToArray(),
            new(0m, total, [], 0m, 0m, total), [InvoiceDeclaration.NotRegisteredUnderGst], InvoiceSignatureMode.AuthorisedSignatory, "component-split/1"));
        db.AddRange(customer, item, batch, stock, sale);
        if (paid < total) db.Add(CustomerLedgerEntry.Create(shop, customer.Id, sale.Id, CustomerLedgerEntryType.SaleDue, total - paid, sale.BusinessDate, null, actor).Value);
        await db.SaveChangesAsync();
        var rollout = factory.Services.GetRequiredService<IOptionsMonitor<SaleCancellationOptions>>().CurrentValue;
        rollout.Enabled = true; rollout.AllowedShopIds = [shop];
        return new(client, shop, actor, sale, batch.Id, customer.Id);
    }
    private static object Request(Seed s, string key = "cancel-key", uint? version = null, bool goods = true, bool? cash = null, string reason = "Mistaken entry") => new { operationKey = key, expectedVersion = version ?? s.Sale.Version, reason, goodsRecoveredConfirmed = goods, cashRefundConfirmed = cash ?? s.Sale.PaidAmount > 0m };

    [Theory]
    [InlineData(8, 8, 200, 1, 0)]
    [InlineData(9, 9, -200, 0, 1)]
    [InlineData(8, 9, 0, 1, 1)]
    public async Task Reports_DatedCancellationKeepsOriginalDocumentCount(int from, int to, decimal net, int invoices, int cancellations)
    {
        factory.Services.GetRequiredService<SettableClock>().Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
        var seed = await SeedAsync(80m);
        factory.Services.GetRequiredService<SettableClock>().Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
        var response = await seed.Client.PostAsJsonAsync($"/api/sales/{seed.Sale.Id}/cancellations", Request(seed));
        response.EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE customers SET name = 'Changed customer' WHERE id = {seed.CustomerId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory_batches SET cost_price = 999 WHERE id = {seed.BatchId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE shops SET name = 'Changed shop' WHERE id = {seed.ShopId}");
        var repository = scope.ServiceProvider.GetRequiredService<ISaleRepository>();
        var start = new DateOnly(2026, 11, from);
        var end = new DateOnly(2026, 11, to);
        var summary = await repository.GetHistorySummaryAsync(seed.ShopId, start, end);
        Assert.Equal(net, summary.PeriodSales);
        Assert.Equal(invoices, summary.InvoiceCount);
        Assert.Equal(0m, summary.RefundAmount);
        Assert.Equal(cancellations, summary.CancellationCount);
        Assert.Equal(cancellations * 200m, summary.CancellationAmount);
        var trend = await repository.GetDailySalesTrendAsync(seed.ShopId, start, end);
        Assert.Equal(net, trend.Sum(t => t.GrossSales - t.ReturnAmount - t.CancellationAmount));
        var metrics = await repository.GetCustomerSalesMetricsAsync(seed.ShopId, [seed.CustomerId],
            scope.ServiceProvider.GetRequiredService<IBusinessClock>().GetUtcWindow(start, end, "Asia/Kolkata"));
        Assert.Equal(1, metrics[seed.CustomerId].PurchaseCount);
        Assert.Equal(0m, metrics[seed.CustomerId].LifetimeRevenue);
        Assert.Equal(net, metrics[seed.CustomerId].CurrentMonthRevenue);
        var report = await scope.ServiceProvider.GetRequiredService<Intelibill.Application.Features.Sales.Queries.GetProfitLossReport.ProfitLossReportBuilder>()
            .BuildAsync(seed.ShopId, "Asia/Kolkata", start, end, null, null, CancellationToken.None);
        Assert.Equal(net, report.Summary.Economics!.NetSalesExcludingTax);
        Assert.Equal(cancellations, report.Items.Count(x => x.RowType == "saleCancellation"));
        Assert.Equal(net * .6m, report.Summary.Economics.Cogs);
        Assert.Equal(0m, report.Summary.Economics.StockLoss);
        var dataset = await scope.ServiceProvider.GetRequiredService<Intelibill.Application.Features.Exports.Sales.Services.ISalesExportDatasetBuilder>()
            .BuildAsync((await db.Shops.FindAsync(seed.ShopId))!, (await db.Users.FindAsync(seed.ActorId))!, start, end, "lineItems", CancellationToken.None);
        Assert.Equal(invoices, dataset.SummaryRows.Count);
        Assert.Equal(cancellations, dataset.CancellationAdjustments.Count);
        Assert.Equal(net, dataset.TaxBreakup.Sum(t => t.NetTaxableAmount));
        Assert.Empty(dataset.ReturnAdjustments);
        foreach (var adjustment in dataset.CancellationAdjustments)
        {
            Assert.Equal("Frozen buyer", adjustment.CustomerName);
            Assert.Equal("Frozen seller", adjustment.Seller.LegalName);
            Assert.Equal(120m, adjustment.RecordedCogs);
            Assert.Equal("Frozen sugar", Assert.Single(adjustment.Lines).Description);
            Assert.Equal("KGS", Assert.Single(adjustment.Lines).Uqc);
        }
        foreach (var invoice in dataset.SummaryRows)
        {
            Assert.Equal("Cancelled", invoice.Status);
            Assert.Equal("Frozen buyer", invoice.CustomerName);
            Assert.NotNull(invoice.CancellationReference);
            Assert.Equal(200m, invoice.TotalAmount);
        }
        var dashboard = await seed.Client.GetAsync($"/api/dashboard?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}");
        dashboard.EnsureSuccessStatusCode();
        var dash = await dashboard.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(cancellations, dash.GetProperty("cancellationCount").GetInt32());
        Assert.Equal(net, dash.GetProperty("salesRevenue").GetDecimal());
        Assert.False(dash.GetProperty("hasNoSalesActivity").GetBoolean());
        Assert.Equal(net, dash.GetProperty("salesTrendSeries").EnumerateArray().Sum(t => t.GetProperty("netAmount").GetDecimal()));
        if (from == 9)
        {
            Assert.Equal(-200m, dash.GetProperty("netProfitChangePercent").GetDecimal());
            Assert.Equal(-200m, dash.GetProperty("grossTradingProfitChangePercent").GetDecimal());
        }
    }
    [Fact]
    public async Task FractionalCancellation_PreservesFrozenCostAndHasNoResidualAcrossDates()
    {
        factory.Services.GetRequiredService<SettableClock>().Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
        var seed = await SeedAsync(66.6m, split: true, cost: 1.01m, quantity: .666m);
        factory.Services.GetRequiredService<SettableClock>().Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
        (await seed.Client.PostAsJsonAsync($"/api/sales/{seed.Sale.Id}/cancellations", Request(seed))).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var builder = scope.ServiceProvider.GetRequiredService<Intelibill.Application.Features.Sales.Queries.GetProfitLossReport.ProfitLossReportBuilder>();
        var cancelled = await builder.BuildAsync(seed.ShopId, "Asia/Kolkata", new(2026, 11, 9), new(2026, 11, 9), "saleCancellation", null, CancellationToken.None);
        Assert.Equal(-.67266m, cancelled.Summary.TotalCost);
        Assert.Equal(-.68m, cancelled.Summary.Economics!.Cogs);
        Assert.Equal(0, cancelled.Summary.ReturnCount);
        var both = await builder.BuildAsync(seed.ShopId, "Asia/Kolkata", new(2026, 11, 8), new(2026, 11, 9), "all", null, CancellationToken.None);
        Assert.Equal(0m, both.Summary.TotalCost);
        Assert.Equal(0m, both.Summary.Economics!.Cogs);
        Assert.Equal(0m, both.Summary.Economics.NetSalesExcludingTax);
    }

}
