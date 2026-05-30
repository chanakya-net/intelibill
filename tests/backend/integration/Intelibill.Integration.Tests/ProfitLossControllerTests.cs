using ClosedXML.Excel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ProfitLossControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
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

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniqueEmail() => $"pl-{Guid.NewGuid():N}@test.com";
    private static string UniqueBarcode() => $"PL-{Guid.NewGuid():N}";

    private static async Task<(string AccessToken, string Email)> RegisterAsync(HttpClient client)
    {
        var email = UniqueEmail();
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email,
            password = "Pass123!Aa",
            firstName = "PL",
            lastName = "Tester",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("accessToken").GetString()!, email);
    }

    private static async Task<(Guid ShopId, string OwnerToken)> CreateShopAsync(HttpClient client, string token, string? name = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = name ?? $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<(Guid UserId, string Email, string Password)> AddShopUserAsync(
        HttpClient client,
        string ownerToken,
        Guid shopId,
        string role)
    {
        var email = UniqueEmail();
        var password = "StaffPass1!";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email,
            firstName = "Shop",
            lastName = role,
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
            password,
            confirmPassword = password,
            role,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("userId").GetGuid(), email, password);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email,
            password,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private string CreateOwnerTokenWithoutShop()
    {
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("active_shop_role", "Owner"),
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<Guid> SetupInventoryAsync(HttpClient client, string token, string barcode)
    {
        const decimal quantity = 50m;

        using var supplierRequest = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        supplierRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        supplierRequest.Content = JsonContent.Create(new
        {
            name = "PL Supplier",
            contactPerson = "PL Contact",
            contactPhone = "+919876543211",
            address = "Supplier Street",
            city = "City",
            state = "State",
            pin = "560002",
            isPreferred = true,
        });
        var supplierResponse = await client.SendAsync(supplierRequest);
        supplierResponse.EnsureSuccessStatusCode();
        var supplierBody = await supplierResponse.Content.ReadFromJsonAsync<JsonElement>();
        var supplierId = supplierBody.GetProperty("supplierId").GetGuid();

        using var itemRequest = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        itemRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        itemRequest.Content = JsonContent.Create(new
        {
            name = "PL Item",
            barcode,
            uom = "PCS",
            supplierId,
            isActive = true,
        });
        var itemResponse = await client.SendAsync(itemRequest);
        itemResponse.EnsureSuccessStatusCode();

        using var inventoryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        inventoryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        inventoryRequest.Content = JsonContent.Create(new
        {
            itemName = "PL Item",
            barcode,
            itemDescription = "Test Item",
            uom = "PCS",
            batchNumber = "B-001",
            quantity = quantity,
            totalPurchaseCost = 80m * quantity,
            salesPrice = 100m,
            mrp = 120m,
            taxRatePercent = 0m,
            taxIncluded = false,
            expiryDate = (string?)null,
            manufacturingDate = (string?)null,
            supplierId,
            referenceNumber = "REF-001",
            notes = "Test Note",
            performedAt = DateTimeOffset.UtcNow,
        });
        var inventoryResponse = await client.SendAsync(inventoryRequest);
        inventoryResponse.EnsureSuccessStatusCode();
        var inventoryBody = await inventoryResponse.Content.ReadFromJsonAsync<JsonElement>();
        return inventoryBody.GetProperty("inventoryBatchId").GetGuid();
    }

    private async Task SetSaleSoldAtAsync(Guid saleId, DateTimeOffset soldAt)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.SingleAsync(s => s.Id == saleId);
        db.Entry(sale).Property(nameof(Sale.SoldAt)).CurrentValue = soldAt;
        await db.SaveChangesAsync();
    }

    private async Task<(Guid SaleId, string InvoiceNumber, Guid SaleItemId)> CreateSaleAsync(
        HttpClient client,
        string token,
        string barcode,
        Guid batchId,
        string customerName,
        DateTimeOffset? soldAt = null)
    {
        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName,
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 100m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "PL Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);
        saleResponse.EnsureSuccessStatusCode();
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        if (soldAt is not null)
        {
            await SetSaleSoldAtAsync(saleId, soldAt.Value);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId);
        return (sale.Id, sale.InvoiceNumber, sale.Items.Single().Id);
    }

    private async Task<string> CreateReturnAsync(
        HttpClient client,
        string token,
        Guid saleId,
        Guid saleItemId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Return note",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 100m,
                    notes = "Return note",
                },
            },
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.SaleReturns
            .Where(r => r.SaleId == saleId)
            .OrderByDescending(r => r.ProcessedAt)
            .Select(r => r.ReturnNumber)
            .FirstAsync();
    }

    private static async Task<(Guid AdjustmentId, string AdjustmentNumber)> CreateAdjustmentAsync(
        HttpClient client,
        string token,
        Guid batchId,
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason,
        decimal quantity,
        string notes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/batches/{batchId}/adjust");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            direction,
            reason,
            quantity,
            performedAt = (DateTimeOffset?)null,
            notes,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("adjustmentId").GetGuid(), body.GetProperty("adjustmentNumber").GetString()!);
    }

    private static async Task VoidAdjustmentAsync(HttpClient client, string token, Guid adjustmentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/adjustments/{adjustmentId}/void");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { reason = "Entered by mistake" });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> GetReportAsync(HttpClient client, string token, string query = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/profit-loss{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> ExportProfitLossAsync(HttpClient client, string token, string query = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/exports/profit-loss{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string GetFileName(HttpResponseMessage response)
    {
        var contentDisposition = response.Content.Headers.ContentDisposition;
        return contentDisposition?.FileNameStar?.Trim('"') ?? contentDisposition?.FileName?.Trim('"') ?? string.Empty;
    }

    [Fact]
    public async Task GetProfitLossReport_ReturnsPagedContractAndDefaultsLastSevenDays()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token, "PL Default Shop");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var barcode = UniqueBarcode();
        var batchId = await SetupInventoryAsync(client, ownerToken, barcode);

        var currentSale = await CreateSaleAsync(client, ownerToken, barcode, batchId, "Current Customer");
        var oldSale = await CreateSaleAsync(client, ownerToken, barcode, batchId, "Old Customer", DateTimeOffset.UtcNow.AddDays(-8));

        var report = await GetReportAsync(client, ownerToken);

        Assert.Equal(JsonValueKind.Array, report.GetProperty("items").ValueKind);
        Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(1, report.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, report.GetProperty("pageNumber").GetInt32());
        Assert.Equal(20, report.GetProperty("pageSize").GetInt32());
        Assert.Equal(today.AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), report.GetProperty("appliedFilters").GetProperty("from").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), report.GetProperty("appliedFilters").GetProperty("to").GetString());
        Assert.Equal("all", report.GetProperty("appliedFilters").GetProperty("type").GetString());
        Assert.Equal(20m, report.GetProperty("summary").GetProperty("netProfitAfterTax").GetDecimal());
        Assert.Equal(100m, report.GetProperty("summary").GetProperty("revenueIncludingTax").GetDecimal());
        Assert.Equal(80m, report.GetProperty("summary").GetProperty("totalCost").GetDecimal());
        Assert.Equal(25m, report.GetProperty("summary").GetProperty("averageMarginPercent").GetDecimal());

        var item = report.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("INV-", item.GetProperty("referenceNumber").GetString()![..4]);
        Assert.Equal(25m, item.GetProperty("marginPercent").GetDecimal());
        Assert.Equal(20m, item.GetProperty("profitAfterTax").GetDecimal());
        Assert.Equal(80m, item.GetProperty("totalCost").GetDecimal());
        Assert.Equal(currentSale.InvoiceNumber, item.GetProperty("referenceNumber").GetString());
    }

    [Fact]
    public async Task GetProfitLossReport_ManagerCanAccessReport()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token, "PL Manager Shop");
        var (_, managerEmail, managerPassword) = await AddShopUserAsync(client, ownerToken, shopId, "Manager");
        var managerToken = await LoginAsync(client, managerEmail, managerPassword);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/profit-loss");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProfitLossReport_StaffGetsForbidden()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token, "PL Staff Shop");
        var (_, staffEmail, staffPassword) = await AddShopUserAsync(client, ownerToken, shopId, "Staff");
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/profit-loss");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetProfitLossReport_WithoutAuth_Returns401()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/sales/profit-loss");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfitLossReport_WithoutActiveShop_Returns400()
    {
        using var client = CreateClient();
        var token = CreateOwnerTokenWithoutShop();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/profit-loss");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetProfitLossReport_OtherShopIsolation_ReturnsEmpty()
    {
        using var client = CreateClient();
        var (tokenA, _) = await RegisterAsync(client);
        var (_, ownerTokenA) = await CreateShopAsync(client, tokenA, "PL Shop A");
        var barcode = UniqueBarcode();
        var batchId = await SetupInventoryAsync(client, ownerTokenA, barcode);
        await CreateSaleAsync(client, ownerTokenA, barcode, batchId, "Shop A Customer");

        var (tokenB, _) = await RegisterAsync(client);
        var (_, ownerTokenB) = await CreateShopAsync(client, tokenB, "PL Shop B");

        var report = await GetReportAsync(client, ownerTokenB);

        Assert.Equal(0, report.GetProperty("totalCount").GetInt32());
        Assert.Empty(report.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetProfitLossReport_PaginatesAndSummarizesAcrossFilteredRows()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token, "PL Paging Shop");
        var barcode = UniqueBarcode();
        var batchId = await SetupInventoryAsync(client, ownerToken, barcode);

        var sale1 = await CreateSaleAsync(client, ownerToken, barcode, batchId, "Alpha", DateTimeOffset.UtcNow.AddDays(-1));
        var sale2 = await CreateSaleAsync(client, ownerToken, barcode, batchId, "Beta");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        var reportPage1 = await GetReportAsync(client, ownerToken, $"?from={yesterday:yyyy-MM-dd}&to={today:yyyy-MM-dd}&page=1&pageSize=1");
        var reportPage2 = await GetReportAsync(client, ownerToken, $"?from={yesterday:yyyy-MM-dd}&to={today:yyyy-MM-dd}&page=2&pageSize=1");

        Assert.Equal(2, reportPage1.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, reportPage1.GetProperty("items").GetArrayLength());
        Assert.Equal(2, reportPage1.GetProperty("summary").GetProperty("invoiceCount").GetInt32());
        Assert.Equal(40m, reportPage1.GetProperty("summary").GetProperty("netProfitAfterTax").GetDecimal());
        Assert.Equal(200m, reportPage1.GetProperty("summary").GetProperty("revenueIncludingTax").GetDecimal());
        Assert.Equal(160m, reportPage1.GetProperty("summary").GetProperty("totalCost").GetDecimal());
        Assert.Equal(25m, reportPage1.GetProperty("summary").GetProperty("averageMarginPercent").GetDecimal());
        Assert.Equal(sale2.InvoiceNumber, reportPage1.GetProperty("items").EnumerateArray().Single().GetProperty("referenceNumber").GetString());
        Assert.Equal(sale1.InvoiceNumber, reportPage2.GetProperty("items").EnumerateArray().Single().GetProperty("referenceNumber").GetString());
    }

    [Fact]
    public async Task GetProfitLossReport_AppliesTypeAndSearchFilters()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token, "PL Search Shop");
        var barcode = UniqueBarcode();
        var batchId = await SetupInventoryAsync(client, ownerToken, barcode);

        var sale = await CreateSaleAsync(client, ownerToken, barcode, batchId, "Customer One");
        var returnNumber = await CreateReturnAsync(client, ownerToken, sale.SaleId, sale.SaleItemId);
        var (_, adjustmentNumber) = await CreateAdjustmentAsync(client, ownerToken, batchId, InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Damaged, 1m, "Damaged stock");

        var customerSearch = await GetReportAsync(client, ownerToken, "?search=Customer");
        var returnSearch = await GetReportAsync(client, ownerToken, $"?type=saleReturn&search={returnNumber}");
        var adjustmentSearch = await GetReportAsync(client, ownerToken, $"?type=inventoryAdjustment&search={adjustmentNumber}");
        var numericSearch = await GetReportAsync(client, ownerToken, "?search=80");

        Assert.Equal(2, customerSearch.GetProperty("totalCount").GetInt32());
        Assert.True(customerSearch.GetProperty("items").EnumerateArray().All(row => row.GetProperty("referenceNumber").GetString()!.Contains("INV-")));
        Assert.Single(returnSearch.GetProperty("items").EnumerateArray());
        Assert.Equal("SaleReturn", returnSearch.GetProperty("items").EnumerateArray().Single().GetProperty("rowType").GetString());
        Assert.Single(adjustmentSearch.GetProperty("items").EnumerateArray());
        Assert.Equal("InventoryAdjustment", adjustmentSearch.GetProperty("items").EnumerateArray().Single().GetProperty("rowType").GetString());
        Assert.Single(numericSearch.GetProperty("items").EnumerateArray());
        Assert.Equal(80m, numericSearch.GetProperty("items").EnumerateArray().Single().GetProperty("totalCost").GetDecimal());
    }

    [Fact]
    public async Task GetProfitLossReport_ReturnsReturnForSaleOutsideDefaultWindow()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token, "PL Cross Range Shop");
        var barcode = UniqueBarcode();
        var batchId = await SetupInventoryAsync(client, ownerToken, barcode);

        var oldSale = await CreateSaleAsync(client, ownerToken, barcode, batchId, "Old Customer", DateTimeOffset.UtcNow.AddDays(-8));
        var returnNumber = await CreateReturnAsync(client, ownerToken, oldSale.SaleId, oldSale.SaleItemId);

        var report = await GetReportAsync(client, ownerToken);
        var item = Assert.Single(report.GetProperty("items").EnumerateArray());

        Assert.Equal(1, report.GetProperty("totalCount").GetInt32());
        Assert.Equal("SaleReturn", item.GetProperty("rowType").GetString());
        Assert.Equal(oldSale.SaleId, item.GetProperty("saleId").GetGuid());
        Assert.Equal($"{oldSale.InvoiceNumber} / {returnNumber}", item.GetProperty("referenceNumber").GetString());
    }

    [Fact]
    public async Task GetProfitLossReport_SearchesAcrossAdjustedAmountAndCustomerName()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token, "PL Filter Shop");
        var barcode = UniqueBarcode();
        var batchId = await SetupInventoryAsync(client, ownerToken, barcode);
        await CreateSaleAsync(client, ownerToken, barcode, batchId, "Search Customer");

        var report = await GetReportAsync(client, ownerToken, "?search=Search");

        Assert.Equal(1, report.GetProperty("totalCount").GetInt32());
        Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal("Search Customer", report.GetProperty("items").EnumerateArray().Single().GetProperty("partyName").GetString());
    }

    [Fact]
    public async Task ExportProfitLoss_WithoutAuth_Returns401()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/exports/profit-loss");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExportProfitLoss_StaffGetsForbidden()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (shopId, ownerToken) = await CreateShopAsync(client, token, "PL Export Staff Shop");
        var (_, staffEmail, staffPassword) = await AddShopUserAsync(client, ownerToken, shopId, "Staff");
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/exports/profit-loss");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExportProfitLoss_UsesActiveFiltersAndExportsAllMatchingRows()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token, "PL Export Shop");
        var barcode = UniqueBarcode();
        var batchId = await SetupInventoryAsync(client, ownerToken, barcode);

        for (var index = 0; index < 21; index++)
        {
            await CreateSaleAsync(client, ownerToken, barcode, batchId, $"Customer {index + 1}", DateTimeOffset.UtcNow.AddMinutes(-index));
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await ExportProfitLossAsync(
            client,
            ownerToken,
            $"?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&type=sale&search=Customer&format=xlsx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);

        var fileName = GetFileName(response);
        Assert.Contains("pl-export-shop-profit-loss", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".xlsx", fileName, StringComparison.OrdinalIgnoreCase);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Profit & Loss");
        var headerRow = sheet.RowsUsed().First(row => row.Cell(1).GetString() == "Reference").RowNumber();
        var dataRows = sheet.RowsUsed().Where(row => row.RowNumber() > headerRow && !string.IsNullOrWhiteSpace(row.Cell(1).GetString())).ToList();

        Assert.Equal(21, dataRows.Count);
        Assert.All(dataRows, row => Assert.StartsWith("INV-", row.Cell(1).GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportProfitLoss_AppliesTypeAndSearchFilters()
    {
        using var client = CreateClient();
        var (token, _) = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token, "PL Export Filter Shop");
        var barcode = UniqueBarcode();
        var batchId = await SetupInventoryAsync(client, ownerToken, barcode);

        var sale = await CreateSaleAsync(client, ownerToken, barcode, batchId, "Filter Customer");
        var returnNumber = await CreateReturnAsync(client, ownerToken, sale.SaleId, sale.SaleItemId);
        var (_, adjustmentNumber) = await CreateAdjustmentAsync(client, ownerToken, batchId, InventoryAdjustmentDirection.Decrease, InventoryAdjustmentReason.Damaged, 1m, "Damaged stock");

        var response = await ExportProfitLossAsync(
            client,
            ownerToken,
            $"?from={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}&to={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}&type=saleReturn&search={returnNumber}&format=xlsx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Profit & Loss");
        var headerRow = sheet.RowsUsed().First(row => row.Cell(1).GetString() == "Reference").RowNumber();
        var dataRows = sheet.RowsUsed().Where(row => row.RowNumber() > headerRow && !string.IsNullOrWhiteSpace(row.Cell(1).GetString())).ToList();

        Assert.Single(dataRows);
        Assert.Equal($"{sale.InvoiceNumber} / {returnNumber}", dataRows[0].Cell(1).GetString());
        Assert.Contains("Filter Customer", dataRows[0].Cell(4).GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(adjustmentNumber, dataRows[0].Cell(1).GetString(), StringComparison.OrdinalIgnoreCase);
    }
}
