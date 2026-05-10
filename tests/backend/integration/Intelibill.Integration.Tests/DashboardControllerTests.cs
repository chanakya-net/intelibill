using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class DashboardControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"dash-{Guid.NewGuid():N}@test.com";
    private static string UniqueBarcode() => $"DASH-{Guid.NewGuid():N}";

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Dash",
            lastName = "Tester",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}"
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<string> CreateShopAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Dash Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task GetDashboard_WithoutAuth_Returns401()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_WithoutShop_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_WithNoData_ReturnsEmptyDashboard()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("hasNoSalesActivity").GetBoolean());
        Assert.Equal(0, body.GetProperty("salesCount").GetInt32());
        Assert.Equal(0, body.GetProperty("runningLowStockCount").GetInt32());
        Assert.Equal(0, body.GetProperty("criticalStockCount").GetInt32());
    }

    [Fact]
    public async Task GetDashboard_WithSales_ReturnsKpis()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        using var inventoryRequest = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        inventoryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        inventoryRequest.Content = JsonContent.Create(new
        {
            itemName = "Dashboard Item",
            barcode,
            itemDescription = (string?)null,
            uom = "PCS",
            batchNumber = "B-001",
            quantity = 20m,
            costPrice = 50m,
            mrp = 80m,
            salesPrice = 75m,
            taxRatePercent = 5m,
            taxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        var inventoryResponse = await client.SendAsync(inventoryRequest);
        inventoryResponse.EnsureSuccessStatusCode();
        var inventoryBody = await inventoryResponse.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = inventoryBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            customerId = (Guid?)null,
            customerName = "Dash Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 78.75m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Dashboard Item",
                    quantity = 1m,
                    costPrice = 50m,
                    salesPrice = 75m,
                    mrp = 80m,
                    taxRatePercent = 5m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        using var dashRequest = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        dashRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var dashResponse = await client.SendAsync(dashRequest);

        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var body = await dashResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("hasNoSalesActivity").GetBoolean());
        Assert.Equal(1, body.GetProperty("salesCount").GetInt32());
    }

    [Fact]
    public async Task GetDashboard_WithAdjustmentLosses_IncludesActiveDecreaseOnlyInFinancialKpis()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var batchId = await CreateInboundAsync(client, ownerToken, quantity: 10m, costPrice: 50m);

        await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 2m,
            performedAt: DateTimeOffset.UtcNow,
            notes: "Damaged");
        await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Increase,
            InventoryAdjustmentReason.FoundStock,
            quantity: 1m,
            performedAt: DateTimeOffset.UtcNow,
            notes: "Found stock");
        var voided = await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Expired,
            quantity: 1m,
            performedAt: DateTimeOffset.UtcNow,
            notes: "Expired");
        await VoidAdjustmentAsync(client, ownerToken, voided);

        using var dashRequest = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        dashRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var dashResponse = await client.SendAsync(dashRequest);

        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var body = await dashResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("hasNoSalesActivity").GetBoolean());
        Assert.Equal(100m, body.GetProperty("wastageCost").GetDecimal());
        Assert.Equal(-100m, body.GetProperty("profitBeforeTax").GetDecimal());
        Assert.Equal(-100m, body.GetProperty("profitAfterTax").GetDecimal());
        Assert.Contains(
            body.GetProperty("profitTrendSeries").EnumerateArray(),
            point => point.GetProperty("date").GetDateTime().Date == DateTime.Now.Date
                && point.GetProperty("profitAfterTax").GetDecimal() == -100m);
    }

    [Fact]
    public async Task GetDashboard_WithLast7DaysAdjustmentLosses_IncludesOnlyInRangeActiveDecreaseAdjustments()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var batchId = await CreateInboundAsync(client, ownerToken, quantity: 10m, costPrice: 50m);

        var today = DateOnly.FromDateTime(DateTimeOffset.Now.DateTime);
        var startDate = today.AddDays(-6);
        var outsidePerformedAt = DateTimeOffset.Now.AddDays(-7).ToUniversalTime();
        var inRangePerformedAt = DateTimeOffset.Now.AddMinutes(-30).ToUniversalTime();
        var inRangeIncreasePerformedAt = DateTimeOffset.Now.AddMinutes(-20).ToUniversalTime();
        var inRangeVoidedPerformedAt = DateTimeOffset.Now.AddMinutes(-10).ToUniversalTime();

        await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 2m,
            performedAt: inRangePerformedAt,
            notes: "In-range loss");

        await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 1m,
            performedAt: outsidePerformedAt,
            notes: "Out-of-range loss");

        await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Increase,
            InventoryAdjustmentReason.FoundStock,
            quantity: 1m,
            performedAt: inRangeIncreasePerformedAt,
            notes: "In-range increase");

        var voided = await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Expired,
            quantity: 1m,
            performedAt: inRangeVoidedPerformedAt,
            notes: "Voided loss");
        await VoidAdjustmentAsync(client, ownerToken, voided);

        using var dashRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/dashboard?startDate={startDate:yyyy-MM-dd}&endDate={today:yyyy-MM-dd}");
        dashRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var dashResponse = await client.SendAsync(dashRequest);

        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var body = await dashResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("hasNoSalesActivity").GetBoolean());
        Assert.Equal(100m, body.GetProperty("wastageCost").GetDecimal());
        Assert.Equal(-100m, body.GetProperty("profitBeforeTax").GetDecimal());
        Assert.Equal(-100m, body.GetProperty("profitAfterTax").GetDecimal());
        Assert.Contains(
            body.GetProperty("profitTrendSeries").EnumerateArray(),
            point => point.GetProperty("date").GetDateTime().Date == today.ToDateTime(TimeOnly.MinValue).Date
                && point.GetProperty("profitAfterTax").GetDecimal() == -100m);
    }

    [Fact]
    public async Task GetDashboard_WithPreviousPeriodAdjustmentLosses_SubtractsLossesFromPreviousProfit()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var batchId = await CreateInboundAsync(client, ownerToken, quantity: 10m, costPrice: 50m);
        var today = DateOnly.FromDateTime(DateTimeOffset.Now.DateTime);
        var startDate = today.AddDays(-6);
        var endDate = today;
        var previousDate = startDate.AddDays(-1);

        await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 2m,
            performedAt: AtLocalTime(previousDate, new TimeOnly(9, 0)),
            notes: "Previous damaged");

        using var dashRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/dashboard?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        dashRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var dashResponse = await client.SendAsync(dashRequest);

        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var body = await dashResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m, body.GetProperty("profitAfterTax").GetDecimal());
        var previous = body.GetProperty("previousPeriodSummary");

        Assert.Equal(-100m, previous.GetProperty("profitAfterTax").GetDecimal());
    }

    [Fact]
    public async Task GetDashboard_WithDateRange_AdjustmentLossesIncludeOnlyInRangeActiveDecreaseAdjustments()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var today = DateOnly.FromDateTime(DateTimeOffset.Now.DateTime);
        var startDate = today;
        var endDate = today;
        var inRangePerformedAt = DateTimeOffset.Now.AddMinutes(-25).ToUniversalTime();
        var outOfRangePerformedAt = DateTimeOffset.Now.AddDays(-1).AddMinutes(-25).ToUniversalTime();
        var increasePerformedAt = DateTimeOffset.Now.AddMinutes(-20).ToUniversalTime();
        var voidedPerformedAt = DateTimeOffset.Now.AddMinutes(-15).ToUniversalTime();

        var batchId = await CreateInboundAsync(client, ownerToken, quantity: 10m, costPrice: 50m);
        await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 2m,
            performedAt: inRangePerformedAt,
            notes: "In-range loss");
        await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 1m,
            performedAt: outOfRangePerformedAt,
            notes: "Out-of-range loss");
        await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Increase,
            InventoryAdjustmentReason.FoundStock,
            quantity: 1m,
            performedAt: increasePerformedAt,
            notes: "In-range increase");
        var voided = await CreateAdjustmentAsync(
            client,
            ownerToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Expired,
            quantity: 1m,
            performedAt: voidedPerformedAt,
            notes: "Voided loss");
        await VoidAdjustmentAsync(client, ownerToken, voided);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/dashboard?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("hasNoSalesActivity").GetBoolean());
        Assert.Equal(100m, body.GetProperty("wastageCost").GetDecimal());
        Assert.Equal(-100m, body.GetProperty("profitBeforeTax").GetDecimal());
        Assert.Equal(-100m, body.GetProperty("profitAfterTax").GetDecimal());
    }

    [Fact]
    public async Task GetDashboard_AsStaff_ReturnsLimitedData()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var ownerScopedToken = await CreateShopAsync(client, ownerToken);

        var staffEmail = UniqueEmail();
        var staffPassword = "Pass123!Aa";
        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { await GetShopIdFromTokenAsync(client, ownerScopedToken) },
            email = staffEmail,
            firstName = "Staff",
            lastName = "Dash",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email = staffEmail,
            password = staffPassword,
        });
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var staffToken = loginBody.GetProperty("accessToken").GetString()!;

        using var dashRequest = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        dashRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var dashResponse = await client.SendAsync(dashRequest);

        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var body = await dashResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("hasNoSalesActivity").GetBoolean());
    }

    [Fact]
    public async Task GetDashboard_AsStaffWithAdjustmentLosses_HidesFinancialFields()
    {
        using var client = CreateClient();
        var ownerToken = await RegisterAsync(client);
        var ownerScopedToken = await CreateShopAsync(client, ownerToken);
        var shopId = await GetShopIdFromTokenAsync(client, ownerScopedToken);
        var batchId = await CreateInboundAsync(client, ownerScopedToken, quantity: 10m, costPrice: 50m);
        await CreateAdjustmentAsync(
            client,
            ownerScopedToken,
            batchId,
            InventoryAdjustmentDirection.Decrease,
            InventoryAdjustmentReason.Damaged,
            quantity: 2m,
            performedAt: DateTimeOffset.UtcNow,
            notes: "Damaged");

        var staffToken = await AddUserAndLoginAsync(client, ownerScopedToken, shopId, "Staff");

        using var dashRequest = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
        dashRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var dashResponse = await client.SendAsync(dashRequest);

        Assert.Equal(HttpStatusCode.OK, dashResponse.StatusCode);
        var body = await dashResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("hasNoSalesActivity").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("wastageCost").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("profitAfterTax").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("profitTrendSeries").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("previousPeriodSummary").ValueKind);
    }

    private static DateTimeOffset AtLocalTime(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime)).ToUniversalTime();
    }

    private static async Task<Guid> GetShopIdFromTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shops/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().First().GetProperty("shopId").GetGuid();
    }

    private static async Task<Guid> CreateInboundAsync(
        HttpClient client,
        string token,
        decimal quantity,
        decimal costPrice)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            itemName = $"Dashboard Adjustment Item {Guid.NewGuid():N}",
            barcode = UniqueBarcode(),
            itemDescription = (string?)null,
            uom = "PCS",
            batchNumber = $"B-{Guid.NewGuid():N}"[..18],
            quantity,
            costPrice,
            mrp = 80m,
            salesPrice = 75m,
            taxRatePercent = 5m,
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
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("inventoryBatchId").GetGuid();
    }

    private static async Task<Guid> CreateAdjustmentAsync(
        HttpClient client,
        string token,
        Guid batchId,
        InventoryAdjustmentDirection direction,
        InventoryAdjustmentReason reason,
        decimal quantity,
        DateTimeOffset performedAt,
        string notes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/batches/{batchId}/adjust");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            direction,
            reason,
            quantity,
            performedAt,
            notes,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("adjustmentId").GetGuid();
    }

    private static async Task VoidAdjustmentAsync(HttpClient client, string token, Guid adjustmentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/adjustments/{adjustmentId}/void");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { reason = "Dashboard exclusion test" });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> AddUserAndLoginAsync(
        HttpClient client,
        string ownerToken,
        Guid shopId,
        string role)
    {
        var email = UniqueEmail();
        const string password = "Pass123!Aa";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email,
            firstName = role,
            lastName = "Dash",
            phoneNumber = $"+91{Random.Shared.NextInt64(7000000000, 9999999999)}",
            password,
            confirmPassword = password,
            role,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login/email", new
        {
            email,
            password,
        });
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        return loginBody.GetProperty("accessToken").GetString()!;
    }
}
