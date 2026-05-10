using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class DiscountsControllerManagementTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"discounts-{Guid.NewGuid():N}@test.com";

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

    private static async Task<(Guid ShopId, string OwnerToken)> CreateShopAsync(HttpClient client, string token)
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
        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    [Fact]
    public async Task CreateDiscountRule_Returns201_AndCanFetchDetail()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/discounts");
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        create.Content = JsonContent.Create(new
        {
            ruleType = "SalePercentage",
            name = "Launch Promo",
            description = "10% off all sales",
            percentage = 10m,
            thresholdAmount = (decimal?)null,
            inventoryBatchId = (Guid?)null,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
            belowCostConfirmed = false,
            belowCostConfirmationReason = (string?)null,
        });

        var createResponse = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/discounts/{id}");
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var getResponse = await client.SendAsync(get);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var detail = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, detail.GetProperty("id").GetGuid());
        Assert.Equal("Launch Promo", detail.GetProperty("name").GetString());
        Assert.Equal("SalePercentage", detail.GetProperty("ruleType").GetString());
        Assert.True(detail.GetProperty("isActive").GetBoolean());
    }

    [Theory]
    [InlineData("/api/discounts", "GET")]
    [InlineData("/api/discounts", "POST")]
    public async Task ManagementEndpoints_WithoutOwnerOrManagerClaim_Return403(string path, string method)
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new
            {
                ruleType = "SalePercentage",
                name = "Nope",
                description = (string?)null,
                percentage = 10m,
                thresholdAmount = (decimal?)null,
                inventoryBatchId = (Guid?)null,
                startsAt = (DateTimeOffset?)null,
                endsAt = (DateTimeOffset?)null,
                belowCostConfirmed = false,
                belowCostConfirmationReason = (string?)null,
            });
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDiscountRule_WhenDifferentShop_Returns404()
    {
        using var client = CreateClient();

        var tokenA = await RegisterAsync(client);
        var (_, ownerTokenA) = await CreateShopAsync(client, tokenA);
        var ruleId = await CreateSaleRuleAsync(client, ownerTokenA, "ShopA Rule", 5m);

        var tokenB = await RegisterAsync(client);
        var (_, ownerTokenB) = await CreateShopAsync(client, tokenB);

        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/discounts/{ruleId}");
        get.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);

        var response = await client.SendAsync(get);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListDiscountRules_SupportsStatusSearchSortAndPageBounds()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        var activeId = await CreateSaleRuleAsync(client, ownerToken, "Alpha", 5m);
        var disabledId = await CreateSaleRuleAsync(client, ownerToken, "Beta", 7m);
        await DisableRuleAsync(client, ownerToken, disabledId, "End");
        var upcomingId = await CreateSaleRuleAsync(client, ownerToken, "Gamma", 9m, startsAt: DateTimeOffset.UtcNow.AddDays(2), endsAt: null);

        var disabled = await ListAsync(client, ownerToken, "/api/discounts?status=disabled&page=1&pageSize=50");
        Assert.Contains(disabled.Items, i => i.Id == disabledId);
        Assert.DoesNotContain(disabled.Items, i => i.Id == activeId);

        var active = await ListAsync(client, ownerToken, "/api/discounts?status=active&page=1&pageSize=50");
        Assert.Contains(active.Items, i => i.Id == activeId);
        Assert.DoesNotContain(active.Items, i => i.Id == disabledId);
        Assert.DoesNotContain(active.Items, i => i.Id == upcomingId);

        var upcoming = await ListAsync(client, ownerToken, "/api/discounts?status=upcoming&page=1&pageSize=50");
        Assert.Contains(upcoming.Items, i => i.Id == upcomingId);

        var search = await ListAsync(client, ownerToken, "/api/discounts?search=Alpha&sort=name_asc&page=1&pageSize=50");
        Assert.Single(search.Items);
        Assert.Equal(activeId, search.Items[0].Id);

        var sorted = await ListAsync(client, ownerToken, "/api/discounts?sort=name_asc&page=1&pageSize=50");
        var names = sorted.Items.Select(i => i.Name).ToList();
        Assert.Equal(names.OrderBy(x => x).ToList(), names);

        var bounded = await ListAsync(client, ownerToken, "/api/discounts?page=1&pageSize=1000");
        Assert.Equal(100, bounded.PageSize);
    }

    [Fact]
    public async Task CreateDiscountRule_WhenSaleLevelOverlaps_StillCreates()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        var startsAt = DateTimeOffset.UtcNow.AddHours(1);
        var endsAt = startsAt.AddDays(1);

        var first = await CreateSaleRuleAsync(client, ownerToken, "Overlap A", 5m, startsAt, endsAt);
        var second = await CreateSaleRuleAsync(client, ownerToken, "Overlap B", 6m, startsAt, endsAt);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task DisableDiscountRule_SoftDisables_AndShowsInDisabledFilter()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        var ruleId = await CreateSaleRuleAsync(client, ownerToken, "To Disable", 5m);

        using var disable = new HttpRequestMessage(HttpMethod.Post, $"/api/discounts/{ruleId}/disable");
        disable.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        disable.Content = JsonContent.Create(new { reason = "Promo ended" });

        var disableResponse = await client.SendAsync(disable);
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/discounts?status=disabled&page=1&pageSize=50");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var listResponse = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == ruleId);
    }

    [Fact]
    public async Task ReplaceDiscountRule_CreatesNewVersion_AndDisablesOld()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var (_, ownerToken) = await CreateShopAsync(client, token);

        var oldId = await CreateSaleRuleAsync(client, ownerToken, "V1", 5m);

        using var replace = new HttpRequestMessage(HttpMethod.Put, $"/api/discounts/{oldId}");
        replace.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        replace.Content = JsonContent.Create(new
        {
            ruleType = "SalePercentage",
            name = "V2",
            description = "new version",
            percentage = 8m,
            thresholdAmount = (decimal?)null,
            inventoryBatchId = (Guid?)null,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
            belowCostConfirmed = false,
            belowCostConfirmationReason = (string?)null,
            disabledReason = "Updating offer",
        });

        var replaceResponse = await client.SendAsync(replace);
        Assert.Equal(HttpStatusCode.OK, replaceResponse.StatusCode);

        var newRule = await replaceResponse.Content.ReadFromJsonAsync<JsonElement>();
        var newId = newRule.GetProperty("id").GetGuid();
        Assert.NotEqual(oldId, newId);
        Assert.Equal(oldId, newRule.GetProperty("replacesRuleId").GetGuid());

        using var getOld = new HttpRequestMessage(HttpMethod.Get, $"/api/discounts/{oldId}");
        getOld.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var getOldResponse = await client.SendAsync(getOld);
        Assert.Equal(HttpStatusCode.OK, getOldResponse.StatusCode);
        var old = await getOldResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(old.GetProperty("isActive").GetBoolean());
        Assert.Equal(newId, old.GetProperty("replacedByRuleId").GetGuid());
    }

    private static async Task<Guid> CreateSaleRuleAsync(HttpClient client, string token, string name, decimal percentage)
        => await CreateSaleRuleAsync(client, token, name, percentage, startsAt: null, endsAt: null);

    private static async Task<Guid> CreateSaleRuleAsync(
        HttpClient client,
        string token,
        string name,
        decimal percentage,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt)
    {
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/discounts");
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        create.Content = JsonContent.Create(new
        {
            ruleType = "SalePercentage",
            name,
            description = (string?)null,
            percentage,
            thresholdAmount = (decimal?)null,
            inventoryBatchId = (Guid?)null,
            startsAt,
            endsAt,
            belowCostConfirmed = false,
            belowCostConfirmationReason = (string?)null,
        });

        var response = await client.SendAsync(create);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task DisableRuleAsync(HttpClient client, string token, Guid ruleId, string reason)
    {
        using var disable = new HttpRequestMessage(HttpMethod.Post, $"/api/discounts/{ruleId}/disable");
        disable.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        disable.Content = JsonContent.Create(new { reason });
        var response = await client.SendAsync(disable);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<(ListItem[] Items, int PageSize)> ListAsync(HttpClient client, string token, string url)
    {
        using var list = new HttpRequestMessage(HttpMethod.Get, url);
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(list);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var items = body.GetProperty("items")
            .EnumerateArray()
            .Select(i => new ListItem(i.GetProperty("id").GetGuid(), i.GetProperty("name").GetString()!))
            .ToArray();

        return (items, body.GetProperty("pageSize").GetInt32());
    }

    private readonly record struct ListItem(Guid Id, string Name);
}
