using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// What the boundary is for: a query or a write that forgets its own
/// <c>shop_id</c> predicate must come back empty or be refused, rather than
/// reaching another merchant's stock, sales, ledgers or money. Every
/// assertion here runs as the restricted runtime role with a real shop
/// context, and the before/after state is read through the owner identity so
/// a denied write is proved to have changed nothing.
/// </summary>
[Collection("Integration Tests")]
public sealed class TenantRowLevelSecurityTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string SharedPassword = "Pass123!Aa";

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

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string UniquePhone() => $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

    private static async Task<(Guid ShopId, string AccessToken)> CreateShopWithTokenAsync(HttpClient client, string prefix)
    {
        var enrolled = await client.EnrollAsync(new
        {
            email = $"rls-tenant-{prefix}-{Guid.NewGuid():N}@test.com",
            password = SharedPassword,
            firstName = "Verified",
            lastName = "Account",
            phoneNumber = UniquePhone(),
        });
        enrolled.EnsureSuccessStatusCode();
        var account = await enrolled.Content.ReadFromJsonAsync<JsonElement>();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.GetProperty("accessToken").GetString());
        request.Content = JsonContent.Create(new
        {
            name = $"Tenant {prefix} {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        var created = await client.SendAsync(request);
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();

        return (body.GetProperty("activeShopId").GetGuid(), body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<Guid> CreateItemAsync(HttpClient client, string token, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/items");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name,
            barcode = $"ITM-{Guid.NewGuid():N}",
            description = "Isolation fixture",
            uom = "kg",
            isActive = true,
            hsnCode = "10063090",
            defaultTaxRatePercent = 5m,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreatePurchaseOrderAsync(HttpClient client, string token)
    {
        var firstItem = await CreateItemAsync(client, token, "PO Item A");
        var secondItem = await CreateItemAsync(client, token, "PO Item B");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            notes = "Isolation fixture",
            supplierName = "Acme Traders",
            supplierReference = $"SUP-{Guid.NewGuid():N}",
            lines = new[]
            {
                new { itemId = firstItem, description = "Item A", expectedQuantity = 3, unitCost = 100m },
                new { itemId = secondItem, description = "Item B", expectedQuantity = 2, unitCost = 25m },
            },
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("purchaseOrderId").GetGuid();
    }

    /// <summary>Opens a runtime-role connection scoped to one shop, as a request would be.</summary>
    private async Task<NpgsqlConnection> OpenScopedAsync(Guid shopId)
    {
        var connection = new NpgsqlConnection(_factory.RuntimeConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            select set_config('app.current_user_id', @user_id, false);
            select set_config('app.active_shop_id', @shop_id, false);
            """;
        command.Parameters.AddWithValue("user_id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("shop_id", shopId.ToString());
        await command.ExecuteNonQueryAsync();

        return connection;
    }

    private async Task<T> ReadAsOwnerAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task ForeignShop_SelectUpdateDeleteDenied()
    {
        using var client = CreateClient();
        var (victimShop, victimToken) = await CreateShopWithTokenAsync(client, "victim");
        var (attackerShop, _) = await CreateShopWithTokenAsync(client, "attacker");
        var victimItem = await CreateItemAsync(client, victimToken, "Victim Rice");

        await using var attacker = await OpenScopedAsync(attackerShop);

        await using var select = new NpgsqlCommand("select count(*) from items where id = @id", attacker);
        select.Parameters.AddWithValue("id", victimItem);
        Assert.Equal(0L, (long)(await select.ExecuteScalarAsync())!);

        await using var update = new NpgsqlCommand("update items set name = 'Seized' where id = @id", attacker);
        update.Parameters.AddWithValue("id", victimItem);
        Assert.Equal(0, await update.ExecuteNonQueryAsync());

        await using var delete = new NpgsqlCommand("delete from items where id = @id", attacker);
        delete.Parameters.AddWithValue("id", victimItem);
        Assert.Equal(0, await delete.ExecuteNonQueryAsync());

        // The row is untouched and still belongs to the shop that created it.
        Assert.Equal(
            "Victim Rice",
            await ReadAsOwnerAsync<string>("select name from items where id = @id", ("id", victimItem)));
        Assert.Equal(
            victimShop,
            await ReadAsOwnerAsync<Guid>("select shop_id from items where id = @id", ("id", victimItem)));
    }

    [Fact]
    public async Task ForeignShop_InsertRejected()
    {
        using var client = CreateClient();
        var (victimShop, _) = await CreateShopWithTokenAsync(client, "insert-victim");
        var (attackerShop, _) = await CreateShopWithTokenAsync(client, "insert-attacker");

        await using var attacker = await OpenScopedAsync(attackerShop);
        await using var insert = new NpgsqlCommand("insert into items (shop_id) values (@shop)", attacker);
        insert.Parameters.AddWithValue("shop", victimShop);

        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        Assert.Equal(0L, await ReadAsOwnerAsync<long>("select count(*) from items where shop_id = @shop", ("shop", victimShop)));
    }

    [Fact]
    public async Task ForeignParent_InsertRejected()
    {
        using var client = CreateClient();
        var (_, victimToken) = await CreateShopWithTokenAsync(client, "parent-victim");
        var (attackerShop, _) = await CreateShopWithTokenAsync(client, "parent-attacker");
        var victimPurchaseOrder = await CreatePurchaseOrderAsync(client, victimToken);

        var linesBefore = await ReadAsOwnerAsync<long>(
            "select count(*) from purchase_order_lines where purchase_order_id = @po",
            ("po", victimPurchaseOrder));

        await using var attacker = await OpenScopedAsync(attackerShop);
        await using var insert = new NpgsqlCommand(
            "insert into purchase_order_lines (purchase_order_id) values (@po)",
            attacker);
        insert.Parameters.AddWithValue("po", victimPurchaseOrder);

        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        Assert.Equal(
            linesBefore,
            await ReadAsOwnerAsync<long>(
                "select count(*) from purchase_order_lines where purchase_order_id = @po",
                ("po", victimPurchaseOrder)));
    }

    /// <summary>
    /// A handler that forgets its own <c>shop_id</c> filter is the realistic
    /// failure this boundary exists for, so the query here deliberately has no
    /// predicate at all and the database is left to supply one.
    /// </summary>
    [Fact]
    public async Task UnscopedRepositoryQuery_ReturnsOnlyActiveShop()
    {
        using var client = CreateClient();
        var (ownShop, ownToken) = await CreateShopWithTokenAsync(client, "unscoped-own");
        var (otherShop, otherToken) = await CreateShopWithTokenAsync(client, "unscoped-other");
        await CreateItemAsync(client, ownToken, "Mine");
        await CreateItemAsync(client, otherToken, "Theirs");

        Assert.True(
            await ReadAsOwnerAsync<long>("select count(*) from items where shop_id = @shop", ("shop", otherShop)) > 0,
            "The other shop must actually own rows, or this test proves nothing.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_factory.RuntimeConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.OpenConnectionAsync();
        try
        {
            await context.Database.ExecuteSqlAsync(
                $"select set_config('app.active_shop_id', {ownShop.ToString()}, false)");

            // No Where clause: whatever comes back, the database chose it.
            var items = await context.Items.AsNoTracking().ToListAsync();

            Assert.NotEmpty(items);
            Assert.All(items, item => Assert.Equal(ownShop, item.ShopId));
            Assert.DoesNotContain(items, item => item.ShopId == otherShop);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
