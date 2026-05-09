using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class ExpensesControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"expense-{Guid.NewGuid():N}@test.com";

    // Helper: register user, get token
    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/email", new
        {
            email = UniqueEmail(),
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    // Helper: create shop, return owner-scoped token
    private static async Task<string> CreateShopAsync(HttpClient client, string token)
    {
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
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    // Helper: record expense, return response body
    private static async Task<JsonElement> RecordExpenseAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/expenses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            categoryName = "Rent",
            amount = 5000m,
            paidTo = "Landlord",
            description = "Monthly rent",
            expenseDate = new DateOnly(2026, 3, 15),
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Helper: login and get token
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

    [Fact]
    public async Task RecordExpense_CreatesExpenseWithCorrectData()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/expenses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            categoryName = "Utilities",
            amount = 1200.50m,
            paidTo = "Electric Co",
            description = "March electricity bill",
            expenseDate = new DateOnly(2026, 3, 20),
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Utilities", body.GetProperty("categoryName").GetString());
        Assert.Equal(1200.50m, body.GetProperty("amount").GetDecimal());
        Assert.Equal("Electric Co", body.GetProperty("paidTo").GetString());
        Assert.Equal("March electricity bill", body.GetProperty("description").GetString());
        Assert.Equal("2026-03-20", body.GetProperty("expenseDate").GetString());
        Assert.False(body.GetProperty("isVoided").GetBoolean());
        Assert.True(body.GetProperty("id").GetGuid() != Guid.Empty);
    }

    [Fact]
    public async Task GetExpenses_ReturnsRecordedExpense()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var expenseBody = await RecordExpenseAsync(client, ownerToken);
        var expenseId = expenseBody.GetProperty("id").GetGuid();

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/expenses");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();

        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = listBody.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(expenseId, items[0].GetProperty("id").GetGuid());
        Assert.Equal("Rent", items[0].GetProperty("categoryName").GetString());
        Assert.Equal(5000m, items[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task GetExpense_ReturnsCorrectExpense()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var expenseBody = await RecordExpenseAsync(client, ownerToken);
        var expenseId = expenseBody.GetProperty("id").GetGuid();

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/expenses/{expenseId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var detailResponse = await client.SendAsync(detailRequest);
        detailResponse.EnsureSuccessStatusCode();

        var detailBody = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expenseId, detailBody.GetProperty("id").GetGuid());
        Assert.Equal("Rent", detailBody.GetProperty("categoryName").GetString());
        Assert.Equal(5000m, detailBody.GetProperty("amount").GetDecimal());
        Assert.Equal("Landlord", detailBody.GetProperty("paidTo").GetString());
    }

    [Fact]
    public async Task CorrectExpense_VoidsOriginalAndCreatesCorrected()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var expenseBody = await RecordExpenseAsync(client, ownerToken);
        var originalId = expenseBody.GetProperty("id").GetGuid();

        using var correctRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/expenses/{originalId}/correct");
        correctRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        correctRequest.Content = JsonContent.Create(new
        {
            categoryName = "Rent",
            amount = 5500m,
            paidTo = "Landlord",
            description = "Corrected rent amount",
            expenseDate = new DateOnly(2026, 3, 15),
        });

        var correctResponse = await client.SendAsync(correctRequest);
        correctResponse.EnsureSuccessStatusCode();

        var correctBody = await correctResponse.Content.ReadFromJsonAsync<JsonElement>();
        var correctedId = correctBody.GetProperty("id").GetGuid();
        Assert.Equal(originalId, correctBody.GetProperty("originalExpenseId").GetGuid());
        Assert.Equal(5500m, correctBody.GetProperty("amount").GetDecimal());

        // Verify original is voided
        using var originalRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/expenses/{originalId}");
        originalRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var originalResponse = await client.SendAsync(originalRequest);
        originalResponse.EnsureSuccessStatusCode();

        var originalDetail = await originalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(originalDetail.GetProperty("isVoided").GetBoolean());

        // Verify corrected expense exists and is not voided
        using var correctedRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/expenses/{correctedId}");
        correctedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var correctedResponse = await client.SendAsync(correctedRequest);
        correctedResponse.EnsureSuccessStatusCode();

        var correctedDetail = await correctedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(correctedDetail.GetProperty("isVoided").GetBoolean());
        Assert.Equal(5500m, correctedDetail.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task GetExpenseCategories_ReturnsCategory()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        // Record an expense to create a category
        await RecordExpenseAsync(client, ownerToken);

        using var categoriesRequest = new HttpRequestMessage(HttpMethod.Get, "/api/expenses/categories");
        categoriesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var categoriesResponse = await client.SendAsync(categoriesRequest);
        categoriesResponse.EnsureSuccessStatusCode();

        var categoriesBody = await categoriesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var categories = categoriesBody.EnumerateArray().ToList();
        Assert.Single(categories);
        Assert.Equal("Rent", categories[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Rls_UserFromShopB_CannotSeeShopAExpense()
    {
        using var client = CreateClient();

        // Shop A owner creates an expense
        var tokenA = await RegisterAsync(client);
        var ownerTokenA = await CreateShopAsync(client, tokenA);
        var expenseBody = await RecordExpenseAsync(client, ownerTokenA);
        var expenseId = expenseBody.GetProperty("id").GetGuid();

        // Shop B owner tries to access the expense
        var tokenB = await RegisterAsync(client);
        var ownerTokenB = await CreateShopAsync(client, tokenB);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/expenses/{expenseId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Auth_StaffMember_Gets403OnPost()
    {
        using var client = CreateClient();

        // Owner creates shop
        var ownerToken = await RegisterAsync(client);
        var ownerScopedToken = await CreateShopAsync(client, ownerToken);

        // Add staff member to the shop
        var staffEmail = UniqueEmail();
        var staffPassword = "Pass123!Aa";
        using var addUserRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        addUserRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerScopedToken);
        addUserRequest.Content = JsonContent.Create(new
        {
            shopIds = new[] { await GetShopIdFromTokenAsync(client, ownerScopedToken) },
            email = staffEmail,
            firstName = "Staff",
            lastName = "User",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        // Login as staff to get staff-scoped token
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        // Staff tries to record an expense
        using var expenseRequest = new HttpRequestMessage(HttpMethod.Post, "/api/expenses");
        expenseRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        expenseRequest.Content = JsonContent.Create(new
        {
            categoryName = "Supplies",
            amount = 100m,
            paidTo = "Vendor",
            description = "Test",
            expenseDate = new DateOnly(2026, 3, 1),
        });
        var expenseResponse = await client.SendAsync(expenseRequest);

        Assert.Equal(HttpStatusCode.Forbidden, expenseResponse.StatusCode);
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
}
