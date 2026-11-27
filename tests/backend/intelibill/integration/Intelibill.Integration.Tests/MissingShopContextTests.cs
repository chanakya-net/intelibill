using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// An authenticated account with no active shop is an ordinary state, not an
/// error: it is every account between enrolling and creating its first shop,
/// and it is every request while token validation runs. The session interceptor
/// writes an empty string into <c>app.active_shop_id</c> for it, so a policy
/// that casts that value straight to <c>uuid</c> raises 22P02 instead of
/// returning nothing. These tests require the safe answer — no rows, and a
/// refused write — from every table that carries a policy.
/// </summary>
[Collection("Integration Tests")]
public sealed class MissingShopContextTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string InvalidTextRepresentation = "22P02";

    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public Task InitializeAsync() => _factory.InitializeAsync();

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

    /// <summary>
    /// Every table that carried a policy before this work, with the column its
    /// predicate reads — its own <c>shop_id</c>, or the parent whose shop owns it.
    /// </summary>
    public static TheoryData<string, string> PoliciedTables => new()
    {
        { "credit_note_redemptions", "shop_id" },
        { "credit_notes", "shop_id" },
        { "discount_rules", "shop_id" },
        { "purchase_order_lines", "purchase_order_id" },
        { "purchase_order_receipt_lines", "purchase_order_receipt_id" },
        { "purchase_order_receipt_sequences", "shop_id" },
        { "purchase_order_receipts", "shop_id" },
        { "purchase_order_sequences", "shop_id" },
        { "purchase_orders", "shop_id" },
        { "reconciliation_issues", "shop_id" },
        { "service_code_sequences", "shop_id" },
        { "services", "shop_id" },
    };

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    /// <summary>
    /// Opens a runtime-role connection carrying exactly what
    /// <c>PostgresSessionContextInterceptor</c> writes for a signed-in account
    /// with no active shop: a user id, and an empty string for the shop.
    /// </summary>
    private async Task<NpgsqlConnection> OpenWithoutActiveShopAsync(Guid userId)
    {
        var connection = new NpgsqlConnection(_factory.RuntimeConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            select set_config('app.current_user_id', @user_id, false);
            select set_config('app.active_shop_id', '', false);
            """;
        command.Parameters.AddWithValue("user_id", userId.ToString());
        await command.ExecuteNonQueryAsync();

        return connection;
    }

    [Theory]
    [MemberData(nameof(PoliciedTables))]
    public async Task MissingContext_SeesNothingAndCannotWrite(string table, string predicateColumn)
    {
        await using var connection = await OpenWithoutActiveShopAsync(Guid.NewGuid());

        await using var select = new NpgsqlCommand(
            string.Create(CultureInfo.InvariantCulture, $"select count(*) from {table}"),
            connection);
        var visible = (long)(await select.ExecuteScalarAsync())!;
        Assert.Equal(0L, visible);

        // Row-level security evaluates WITH CHECK before table constraints, so a
        // single-column insert is refused by the policy rather than by NOT NULL.
        await using var insert = new NpgsqlCommand(
            string.Create(CultureInfo.InvariantCulture, $"insert into {table} ({predicateColumn}) values (@value)"),
            connection);
        insert.Parameters.AddWithValue("value", Guid.NewGuid());

        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());

        Assert.NotEqual(InvalidTextRepresentation, error.SqlState);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task MissingContext_HidesRowsThatExistForAnotherShop()
    {
        using var client = CreateClient();
        var (userId, shopId) = await CreateShopAsync(client);
        await SeedDiscountRuleAsync(shopId, userId);

        await using var owner = new NpgsqlConnection(_factory.MigratorConnectionString);
        await owner.OpenAsync();
        await using var ownerCount = new NpgsqlCommand(
            "select count(*) from discount_rules where shop_id = @shop",
            owner);
        ownerCount.Parameters.AddWithValue("shop", shopId);
        Assert.Equal(1L, (long)(await ownerCount.ExecuteScalarAsync())!);

        await using var connection = await OpenWithoutActiveShopAsync(userId);
        await using var visibleCount = new NpgsqlCommand("select count(*) from discount_rules", connection);

        Assert.Equal(0L, (long)(await visibleCount.ExecuteScalarAsync())!);
    }

    private static async Task<(Guid UserId, Guid ShopId)> CreateShopAsync(HttpClient client)
    {
        var enrolled = await client.EnrollAsync(new
        {
            email = $"rls-ctx-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Verified",
            lastName = "Account",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
        });
        enrolled.EnsureSuccessStatusCode();
        var account = await enrolled.Content.ReadFromJsonAsync<JsonElement>();
        var userId = account.GetProperty("user").GetProperty("id").GetGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.GetProperty("accessToken").GetString());
        request.Content = JsonContent.Create(new
        {
            name = $"Context Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        var created = await client.SendAsync(request);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();

        return (userId, body.GetProperty("activeShopId").GetGuid());
    }

    /// <summary>
    /// Written through the owner identity, so the row exists for shop
    /// <paramref name="shopId"/> whatever the policies say about reading it.
    /// </summary>
    private async Task SeedDiscountRuleAsync(Guid shopId, Guid userId)
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into discount_rules
                (id, shop_id, rule_type, name, percentage, is_active, below_cost_confirmed, created_by, created_at)
            values
                (@id, @shop, 'ItemPercentage', 'Seeded rule', 10.00, true, false, @user, now())
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("shop", shopId);
        command.Parameters.AddWithValue("user", userId);
        await command.ExecuteNonQueryAsync();
    }
}
