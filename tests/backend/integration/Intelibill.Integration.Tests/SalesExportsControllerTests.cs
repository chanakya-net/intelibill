using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SalesExportsControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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
        AllowAutoRedirect = false
    });

    private static string UniqueEmail() => $"sales-export-{Guid.NewGuid():N}@test.com";

    private static string UniqueBarcode() => $"SALE-{Guid.NewGuid():N}";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email,
            password
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<(Guid ShopId, string OwnerToken)> CreateShopAsync(HttpClient client, string token, string shopName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = shopName,
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
            contactPerson = (string?)null,
            mobileNumber = (string?)null,
            gstNumber = (string?)null
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<JsonElement> AddInventoryAsync(HttpClient client, string token, string barcode, string batchNumber, decimal quantity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            itemName = "Test Item",
            barcode,
            itemDescription = (string?)null,
            uom = "kg",
            batchNumber,
            quantity,
            totalPurchaseCost = 80m * quantity,
            mrp = 120m,
            salesPrice = 100m,
            taxRatePercent = 18m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(Guid SaleId, Guid SaleItemId)> CreateSaleAsync(HttpClient client, string token, string barcode)
    {
        var inboundBody = await AddInventoryAsync(client, token, barcode, "B-001", 5m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 118m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                }
            }
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = body.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);

        return (saleId, sale.Items[0].Id);
    }

    private static async Task<(string Email, string Password, string RoleToken)> AddShopUserAsync(
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
            firstName = "Store",
            lastName = "Member",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
            password,
            confirmPassword = password,
            role
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var token = await LoginAsync(client, email, password);
        return (email, password, token);
    }

    private static async Task<HttpResponseMessage> ExportSalesAsync(
        HttpClient client,
        string token,
        string format,
        string level,
        DateOnly startDate,
        DateOnly endDate)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/exports/sales?format={Uri.EscapeDataString(format)}&level={Uri.EscapeDataString(level)}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string GetFileName(HttpResponseMessage response)
    {
        var contentDisposition = response.Content.Headers.ContentDisposition;
        var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        return fileName?.Trim('"') ?? string.Empty;
    }

    private static string GetSanitizedShopName(string shopName)
    {
        var lower = shopName.ToLowerInvariant();
        var withHyphens = Regex.Replace(lower, @"\s+", "-");
        var safe = Regex.Replace(withHyphens, @"[^a-z0-9\-]", string.Empty);
        var collapsed = Regex.Replace(safe, "-{2,}", "-");
        var trimmed = collapsed.Trim('-');
        return string.IsNullOrWhiteSpace(trimmed) ? "shop" : trimmed;
    }

    [Fact]
    public async Task ExportSales_WithoutAuth_Returns401()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/exports/sales");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExportSales_WithoutActiveShop_Returns403()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/exports/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExportSales_AsStaff_Returns403()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (shopId, ownerScopedToken) = await CreateShopAsync(client, ownerToken, "Staff Shop");

        var staff = await AddShopUserAsync(client, ownerScopedToken, shopId, "Staff");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/exports/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staff.RoleToken);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExportSales_AsOwner_ReturnsSuccess()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken, "Owner Shop");

        var barcode = UniqueBarcode();
        await CreateSaleAsync(client, ownerScopedToken, barcode);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var response = await ExportSalesAsync(
            client,
            ownerScopedToken,
            "xlsx",
            "summary",
            today.AddDays(-1),
            today);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ExportSales_AsManager_ReturnsSuccess()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (shopId, ownerScopedToken) = await CreateShopAsync(client, ownerToken, "Manager Shop");

        var manager = await AddShopUserAsync(client, ownerScopedToken, shopId, "Manager");
        var barcode = UniqueBarcode();
        await CreateSaleAsync(client, ownerScopedToken, barcode);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var response = await ExportSalesAsync(
            client,
            manager.RoleToken,
            "xlsx",
            "summary",
            today.AddDays(-1),
            today);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ExportSales_WithoutDateFilters_UsesDefaultLast30DaysWindow()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken, "Default Window Shop");

        var barcode = UniqueBarcode();
        await CreateSaleAsync(client, ownerScopedToken, barcode);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var defaultStartDate = today.AddDays(-29);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/exports/sales?format=xlsx");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);

        var fileName = GetFileName(response);
        var expectedShopName = GetSanitizedShopName("Default Window Shop");
        var expectedSegment = $"{expectedShopName}-sales-summary-{defaultStartDate:yyyy-MM-dd}-to-{today:yyyy-MM-dd}";
        Assert.Contains(expectedSegment, fileName, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".xlsx", fileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportSales_WithUnsupportedFormat_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken, "Validation Shop");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var response = await ExportSalesAsync(
            client,
            ownerScopedToken,
            "csv",
            "summary",
            today.AddDays(-1),
            today);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExportSales_WithUnsupportedLevel_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken, "Validation Shop");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var response = await ExportSalesAsync(
            client,
            ownerScopedToken,
            "xlsx",
            "detail",
            today.AddDays(-1),
            today);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExportSales_WithStartDateAfterEndDate_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken, "Validation Shop");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var response = await ExportSalesAsync(
            client,
            ownerScopedToken,
            "xlsx",
            "summary",
            today,
            today.AddDays(-1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExportSales_WithDateRangeExceeding366Days_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken, "Validation Shop");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var response = await ExportSalesAsync(
            client,
            ownerScopedToken,
            "xlsx",
            "summary",
            today.AddDays(-367),
            today);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx")]
    [InlineData("pdf", "application/pdf", ".pdf")]
    [InlineData("tallyXml", "application/xml", ".xml")]
    public async Task ExportSales_SupportedFormats_ReturnsExpectedHeadersAndFilename(
        string format,
        string expectedContentType,
        string expectedExtension)
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var shopName = "Sales Export (Main)";
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken, shopName);

        var barcode = UniqueBarcode();
        await CreateSaleAsync(client, ownerScopedToken, barcode);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1);
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var response = await ExportSalesAsync(client, ownerScopedToken, format, "summary", startDate, endDate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Content.Headers.ContentType);

        var fileName = GetFileName(response);
        Assert.NotEmpty(fileName);

        var expectedShopName = GetSanitizedShopName(shopName);
        var fileKind = string.Equals(format, "tallyXml", StringComparison.OrdinalIgnoreCase)
            ? "tally"
            : "summary";

        var expectedSegment = $"{expectedShopName}-sales-{fileKind}-{startDate:yyyy-MM-dd}-to-{endDate:yyyy-MM-dd}";
        Assert.Contains(expectedSegment, fileName, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(expectedExtension, fileName, StringComparison.OrdinalIgnoreCase);

        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ExportSales_TallyXml_ReturnsSalesAndCreditNoteVouchers_ForSeededReturns()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var (_, ownerScopedToken) = await CreateShopAsync(client, ownerToken, "Return Mix Shop");

        var barcode = UniqueBarcode();
        var (saleId, saleItemId) = await CreateSaleAsync(client, ownerScopedToken, barcode);

        using var returnRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns");
        returnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        returnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Return for seeded sale",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                }
            }
        });

        var returnResponse = await client.SendAsync(returnRequest);
        returnResponse.EnsureSuccessStatusCode();

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1);
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var response = await ExportSalesAsync(client, ownerScopedToken, "tallyXml", "summary", startDate, endDate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
        var content = Encoding.UTF8.GetString(bytes);

        Assert.Contains("<VOUCHERTYPE>Sales</VOUCHERTYPE>", content, StringComparison.Ordinal);
        Assert.Contains("<VOUCHERTYPE>Credit Note</VOUCHERTYPE>", content, StringComparison.Ordinal);
    }
}
