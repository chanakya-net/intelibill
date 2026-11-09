using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class DirectoryQueryBudgetTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private readonly DirectoryCommands _commands = new();
    private ApiWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _shop;

    public async Task InitializeAsync()
    {
        _factory = new(fixture, services => { services.AddSingleton<IInterceptor>(_commands); services.AddSingleton<Intelibill.Application.Common.Time.IBusinessClock>(new Intelibill.Infrastructure.Services.Time.BusinessClock(new DirectoryTime())); });
        await _factory.InitializeAsync();
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var enrolled = await _client.EnrollAsync(new { email = $"directory-{Guid.NewGuid():N}@test.com", password = "Pass123!Aa", firstName = "Directory", lastName = "Reader" });
        enrolled.EnsureSuccessStatusCode();
        var token = (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var created = await _client.PostAsJsonAsync("/api/shops", new { name = "Directory", address = "Road", city = "City", state = "Karnataka", pincode = "560001" });
        created.EnsureSuccessStatusCode();
        var scope = await created.Content.ReadFromJsonAsync<JsonElement>();
        _shop = scope.GetProperty("activeShopId").GetGuid();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.GetProperty("accessToken").GetString());
    }

    public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }
    public void Dispose() { _client.Dispose(); _factory.Dispose(); GC.SuppressFinalize(this); }
    private ApplicationDbContext Owner() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_factory.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);
    private async Task<JsonElement> Get(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task Seed(int count = 25)
    {
        await using var db = Owner();
        for (var i = 0; i < count; i++)
        {
            db.Customers.Add(Customer.Create(_shop, $"Same name", $"{i:0000000000}", i == count - 1 ? "Target %_\\ literal" : "Road", i % 2 == 0, i + 1));
            db.Suppliers.Add(Supplier.Create(_shop, "Same name", i == count - 1 ? "Target %_\\ literal" : null, $"{i:0000000000}", "Road", "City", null, null, i % 2 == 0, i % 3 == 0));
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task LegacyDirectory_RetainsArrayShape()
    {
        await Seed();
        Assert.Equal(JsonValueKind.Array, (await Get("/api/customers")).ValueKind);
        Assert.Equal(25, (await Get("/api/customers")).GetArrayLength());
        Assert.Equal(25, (await Get("/api/suppliers?include_system=false")).GetArrayLength());
    }

    [Fact]
    public async Task DirectoryFilter_AppliesBeforePaging_AndLiteralWildcards_AreNotPatterns()
    {
        await Seed();
        foreach (var party in new[] { "customers", "suppliers" })
        {
            var result = await Get($"/api/{party}/directory?search={Uri.EscapeDataString("  TARGET %_\\  ")}&status=active&pageSize=1");
            Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
            Assert.Single(result.GetProperty("items").EnumerateArray());
            Assert.Equal(0, (await Get($"/api/{party}/directory?search=does-not-exist")).GetProperty("totalCount").GetInt32());
        }
        var unassigned = await Get("/api/suppliers/directory?search=unassigned");
        Assert.True(unassigned.GetProperty("items")[0].GetProperty("isSystem").GetBoolean());
        Assert.Equal(0, (await Get("/api/suppliers/directory?search=unassigned&include_system=false")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task SameNameParties_StablePagination_AndDirectorySummary_IsGlobalNotFirstPage()
    {
        await Seed();
        foreach (var party in new[] { "customers", "suppliers" })
        {
            var first = await Get($"/api/{party}/directory?include_system=false&pageSize=20");
            var second = await Get($"/api/{party}/directory?include_system=false&pageNumber=2&pageSize=20");
            var id = party == "customers" ? "customerId" : "supplierId";
            var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(x => x.GetProperty(id).GetGuid()).ToArray();
            Assert.Equal(25, ids.Distinct().Count());
            Assert.Equal(ids.OrderBy(x => x.ToString(), StringComparer.Ordinal), ids);
            var filtered = await Get($"/api/{party}/directory?include_system=false&search=Target&status=active&pageSize=1");
            Assert.Equal(first.GetProperty("summary").GetRawText(), filtered.GetProperty("summary").GetRawText());
            Assert.Equal(25, filtered.GetProperty("summary").GetProperty(party == "customers" ? "totalCustomers" : "totalSuppliers").GetInt32());
            if (party == "customers") Assert.Equal(325m, filtered.GetProperty("summary").GetProperty("totalCreditIssued").GetDecimal());
        }
    }

    [Theory]
    [InlineData("pageNumber=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=bogus")]
    [InlineData("pageNumber=2147483647&pageSize=100")]
    public async Task DirectoryBounds_AreValidated(string query)
    {
        foreach (var party in new[] { "customers", "suppliers" })
        {
            var response = await _client.GetAsync($"/api/{party}/directory?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task PartyById_IsShopScoped_AndDtosMatchLegacy()
    {
        await Seed(1);
        await using var db = Owner();
        var foreignShop = Shop.Create("Other", "Road", "City", "State", "560001", null, null, null);
        var customer = Customer.Create(foreignShop.Id, "Foreign", "123", null);
        var supplier = Supplier.Create(foreignShop.Id, "Foreign", null, null, null, null, null, null, true, false);
        db.AddRange(foreignShop, customer, supplier);
        await db.SaveChangesAsync();
        foreach (var party in new[] { "customers", "suppliers" })
        {
            Assert.Equal(0, (await Get($"/api/{party}/directory?search=Foreign")).GetProperty("totalCount").GetInt32());
            Assert.Equal(1, (await Get($"/api/{party}/directory?include_system=false")).GetProperty("summary").GetProperty(party == "customers" ? "totalCustomers" : "totalSuppliers").GetInt32());
            var legacy = (await Get($"/api/{party}?include_system=false"))[0];
            var id = legacy.GetProperty(party == "customers" ? "customerId" : "supplierId").GetGuid();
            Assert.Equal(legacy.GetRawText(), (await Get($"/api/{party}/{id}")).GetRawText());
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/{party}/{(party == "customers" ? customer.Id : supplier.Id)}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/{party}/{Guid.NewGuid()}")).StatusCode);
        }
    }

    [Fact]
    public async Task DirectorySummary_UsesPositivePartyNets_AndSystemVisibility()
    {
        await Seed(2);
        await using var db = Owner();
        var actor = await db.ShopMemberships.Where(m => m.ShopId == _shop).Select(m => m.UserId).FirstAsync();
        var customers = await db.Customers.Where(p => p.ShopId == _shop).OrderBy(p => p.PhoneNumber).ToArrayAsync();
        var suppliers = await db.Suppliers.Where(p => p.ShopId == _shop && !p.IsSystem).OrderBy(p => p.ContactPersonPhone).ToArrayAsync();
        var system = await db.Suppliers.SingleAsync(p => p.ShopId == _shop && p.IsSystem);
        db.AddRange(
            CustomerLedgerEntry.Create(_shop, customers[0].Id, null, CustomerLedgerEntryType.ReturnCreditReversal, 100, new(2026, 6, 1), null, actor).Value,
            CustomerLedgerEntry.Create(_shop, customers[0].Id, null, CustomerLedgerEntryType.PaymentReceived, 40, new(2026, 6, 1), null, actor).Value,
            CustomerLedgerEntry.Create(_shop, customers[1].Id, null, CustomerLedgerEntryType.PaymentReceived, 200, new(2026, 6, 1), null, actor).Value,
            SupplierLedgerEntry.Create(_shop, suppliers[0].Id, null, SupplierLedgerEntryType.RecordAdjusted, 100, new(2026, 6, 1), null, actor).Value,
            SupplierLedgerEntry.Create(_shop, suppliers[0].Id, null, SupplierLedgerEntryType.PaymentMade, 40, new(2026, 6, 1), null, actor).Value,
            SupplierLedgerEntry.Create(_shop, suppliers[1].Id, null, SupplierLedgerEntryType.RecordAdjusted, -200, new(2026, 6, 1), null, actor).Value,
            SupplierLedgerEntry.Create(_shop, system.Id, null, SupplierLedgerEntryType.RecordAdjusted, 999, new(2026, 6, 1), null, actor).Value);
        await db.SaveChangesAsync();
        var customerSummary = (await Get("/api/customers/directory?pageSize=1&status=active&search=no-match")).GetProperty("summary");
        Assert.Equal(60m, customerSummary.GetProperty("outstandingBalance").GetDecimal());
        Assert.Equal(1, customerSummary.GetProperty("outstandingCount").GetInt32());
        Assert.Equal(60m, (await Get("/api/suppliers/directory?include_system=false&pageSize=1&search=no-match")).GetProperty("summary").GetProperty("pendingPayables").GetDecimal());
        Assert.Equal(1059m, (await Get("/api/suppliers/directory")).GetProperty("summary").GetProperty("pendingPayables").GetDecimal());
    }

    [Fact]
    public async Task MonthlyRevenue_UsesShopLocalBoundaries_AndSeparateCancellationDate()
    {
        await Seed(1);
        await using var db = Owner();
        var actor = await db.ShopMemberships.Where(m => m.ShopId == _shop).Select(m => m.UserId).FirstAsync();
        var customer = await db.Customers.SingleAsync(p => p.ShopId == _shop);
        var start = new DateTimeOffset(2026, 5, 31, 18, 30, 0, TimeSpan.Zero);
        var end = start.AddMonths(1);
        var item = Item.Create(_shop, "Goods", null, "kg", Guid.NewGuid().ToString("N"), true, actor);
        var batch = InventoryBatch.Create(_shop, item.Id, "LOT", 10, 60, 100, 100, 0, false, null, null, null, actor).Value;
        var sale = Sale.Record(_shop, actor, Guid.NewGuid().ToString("N"), "HASH", "CANCELLED",
            [new SaleLineInput(_shop, SaleLineType.Goods, item.Id, batch.Id, null, "Goods", "GOODS", 2, 60, 100, 100, 0, false, false)],
            customer.Id, customer.Name, null, PaymentMethod.Cash, 200, 0, start, BusinessDay.InDefaultZone(start)).Value;
        var movement = StockTransaction.Create(_shop, item.Id, batch.Id, StockTransactionType.SaleCancellation, 2, "SC", null, end.AddSeconds(-1), actor, actor).Value;
        var cancellation = SaleCancellation.Record(new(_shop, sale.Id, actor, Guid.NewGuid().ToString("N"), "Mistaken entry", end.AddSeconds(-1),
            new(2026, 6, 30), "Asia/Kolkata", sale.InvoiceNumber, new(2026, 6, 1), 200, 200, 0, 0, 200, null,
            new("Seller", "Shop", "Address", "29", "Karnataka", null, TaxRegime.Unregistered),
            new(customer.Name, null, null, null, null, null), true, true,
            [new(_shop, sale.Id, sale.Items[0].Id, item.Id, batch.Id, movement.Id, 2, "Goods", "KGS", 60, 200, 120)])).Value;
        db.AddRange(item, batch, sale, movement, cancellation);
        db.Add(Sale.Create(_shop, "IN-MONTH", customer.Id, customer.Name, null, PaymentMethod.Cash, start, 100, 0, 100, 0, []));
        db.Add(Sale.Create(_shop, "BEFORE", customer.Id, customer.Name, null, PaymentMethod.Cash, start.AddSeconds(-1), 999, 0, 999, 0, []));
        db.Add(Sale.Create(_shop, "AFTER", customer.Id, customer.Name, null, PaymentMethod.Cash, end, 777, 0, 777, 0, []));
        await db.SaveChangesAsync();
        var page = await Get("/api/customers/directory");
        Assert.Equal(100m, page.GetProperty("summary").GetProperty("monthlyRevenue").GetDecimal());
        var shown = page.GetProperty("items")[0];
        Assert.Equal(100m, shown.GetProperty("currentMonthRevenue").GetDecimal());
        Assert.Equal(1876m, shown.GetProperty("lifetimeRevenue").GetDecimal());
        Assert.Equal(4, shown.GetProperty("purchaseCount").GetInt32());
        Assert.Equal(shown.GetRawText(), (await Get("/api/customers"))[0].GetRawText());
    }

    [Fact]
    public async Task DirectorySearchLength_IsValidated_AndDefaultsAreBounded()
    {
        await Seed();
        foreach (var party in new[] { "customers", "suppliers" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/{party}/directory?search={new string('x', 201)}")).StatusCode);
            var page = await Get($"/api/{party}/directory");
            Assert.Equal(1, page.GetProperty("pageNumber").GetInt32());
            Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
            Assert.Equal(20, page.GetProperty("items").GetArrayLength());
        }
    }

    [Fact]
    public async Task DirectoryAndIdReads_RejectSuspendedLiveMembership()
    {
        await Seed(1);
        await using var db = Owner();
        var customer = await db.Customers.SingleAsync(p => p.ShopId == _shop);
        var supplier = await db.Suppliers.SingleAsync(p => p.ShopId == _shop && !p.IsSystem);
        var membership = await db.ShopMemberships.SingleAsync(m => m.ShopId == _shop);
        membership.ChangeAuthority(ShopRole.Owner, MembershipStatus.Suspended);
        await db.SaveChangesAsync();
        foreach (var url in new[] { "/api/customers/directory", "/api/suppliers/directory", $"/api/customers/{customer.Id}", $"/api/suppliers/{supplier.Id}" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task EmptyDirectory_ReturnsZeroSummary()
    {
        var customers = await Get("/api/customers/directory");
        var suppliers = await Get("/api/suppliers/directory?include_system=false");
        Assert.Empty(customers.GetProperty("items").EnumerateArray());
        Assert.Empty(suppliers.GetProperty("items").EnumerateArray());
        Assert.All(customers.GetProperty("summary").EnumerateObject(), p => Assert.Equal(0m, p.Value.GetDecimal()));
        Assert.All(suppliers.GetProperty("summary").EnumerateObject(), p => Assert.Equal(0m, p.Value.GetDecimal()));
    }

    [Fact]
    public async Task SupplierPage_ConstantQueryCount()
    {
        await Seed(1);
        _commands.Sql.Clear();
        await Get("/api/suppliers/directory?include_system=false&pageSize=100");
        var one = _commands.Sql.Count;
        await Seed(99);
        _commands.Sql.Clear();
        await Get("/api/suppliers/directory?include_system=false&pageSize=100");
        Assert.Equal(one, _commands.Sql.Count);
        Assert.InRange(one, 1, 16);
        Assert.Contains(_commands.Sql, sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_commands.Sql, sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class DirectoryCommands : DbCommandInterceptor
{
    public List<string> Sql { get; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
}

internal sealed class DirectoryTime : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
}
