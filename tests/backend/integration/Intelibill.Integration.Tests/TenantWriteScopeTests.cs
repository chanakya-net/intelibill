using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Three operations legitimately write or read a tenant table for a shop that
/// is not the request's active one: creating a shop (which writes the new
/// shop's system supplier before the rescope), reading shop details, and
/// writing shop bank details. They acquire a transaction-local scope rather
/// than a bypass flag, and the shop id is always server-derived — never taken
/// from a header, a claim or a request body.
/// </summary>
[Collection("Integration Tests")]
public sealed class TenantWriteScopeTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string SharedPassword = "Pass123!Aa";

    private readonly ApiWebApplicationFactory _factory = new(fixture, maxPoolSize: 1, runtimeIdentityOutsideRequests: true);

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

    private static async Task<(Guid UserId, string Email, string AccessToken)> EnrollAsync(HttpClient client, string prefix)
    {
        var email = $"scope-{prefix}-{Guid.NewGuid():N}@test.com";
        var response = await client.EnrollAsync(new
        {
            email,
            password = SharedPassword,
            firstName = "Verified",
            lastName = "Account",
            phoneNumber = UniquePhone(),
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("user").GetProperty("id").GetGuid(),
            email,
            body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<(Guid ShopId, string AccessToken)> CreateShopAsync(HttpClient client, string token, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name,
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

    /// <summary>Counts through the owner identity, so the policies cannot hide the answer.</summary>
    private async Task<long> CountAsync(string sql, Guid shopId)
    {
        await using var connection = new NpgsqlConnection(_factory.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("shop", shopId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ReadShopContextAsync(ApplicationDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "select coalesce(current_setting('app.active_shop_id', true), '')";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Elevation_RequiresOpenTransaction()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var writeScope = scope.ServiceProvider.GetRequiredService<ITenantWriteScope>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => writeScope.EnterAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("transaction", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Elevation_RevertsAfterCommitAndRollback()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var writeScope = scope.ServiceProvider.GetRequiredService<ITenantWriteScope>();

        // One physical connection for the whole test, so every read below is served
        // by the connection the elevation ran on.
        await context.Database.OpenConnectionAsync(CancellationToken.None);
        try
        {
            var committed = Guid.NewGuid();
            await context.Database.BeginTransactionAsync(CancellationToken.None);
            await writeScope.EnterAsync(committed, CancellationToken.None);
            Assert.Equal(committed.ToString(), await ReadShopContextAsync(context));
            await context.Database.CommitTransactionAsync(CancellationToken.None);

            Assert.Equal(string.Empty, await ReadShopContextAsync(context));

            var rolledBack = Guid.NewGuid();
            await context.Database.BeginTransactionAsync(CancellationToken.None);
            await writeScope.EnterAsync(rolledBack, CancellationToken.None);
            Assert.Equal(rolledBack.ToString(), await ReadShopContextAsync(context));
            await context.Database.RollbackTransactionAsync(CancellationToken.None);

            Assert.Equal(string.Empty, await ReadShopContextAsync(context));
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task CreateFirstShop_SucceedsWithRestrictedRole()
    {
        using var client = CreateClient();
        var (_, _, token) = await EnrollAsync(client, "first");

        var (shopId, _) = await CreateShopAsync(client, token, "First Shop");

        Assert.Equal(
            1L,
            await CountAsync("select count(*) from suppliers where shop_id = @shop and name = 'Unknown Supplier'", shopId));
    }

    [Fact]
    public async Task CreateSecondShop_WhileScopedToFirst_CreatesSystemSupplier()
    {
        using var client = CreateClient();
        var (_, _, token) = await EnrollAsync(client, "second");
        var (first, firstToken) = await CreateShopAsync(client, token, "Scoped First");

        // The request's active shop is still the first shop while the second
        // shop's system supplier — a tenant row — is written.
        var (second, _) = await CreateShopAsync(client, firstToken, "Scoped Second");

        Assert.NotEqual(first, second);
        Assert.Equal(
            1L,
            await CountAsync("select count(*) from suppliers where shop_id = @shop and name = 'Unknown Supplier'", second));
        Assert.Equal(
            1L,
            await CountAsync("select count(*) from suppliers where shop_id = @shop and name = 'Unknown Supplier'", first));
    }

    [Fact]
    public async Task OwnerAdministersNonActiveShop_ReadsAndWritesBankDetails()
    {
        using var client = CreateClient();
        var (_, _, token) = await EnrollAsync(client, "admin");
        var (first, firstToken) = await CreateShopAsync(client, token, "Admin First");
        var (second, secondToken) = await CreateShopAsync(client, firstToken, "Admin Second");

        // Scoped to the second shop, administering the first.
        using var write = new HttpRequestMessage(HttpMethod.Put, $"/api/shops/{first}/bank-details");
        write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
        write.Content = JsonContent.Create(new
        {
            bankName = "State Bank of India",
            accountNumber = "00112233445566",
            accountType = "Current",
            ifscCode = "SBIN0001234",
            accountHolderName = "Admin First",
        });
        var written = await client.SendAsync(write);

        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        var writtenBody = await written.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("State Bank of India", writtenBody.GetProperty("bankName").GetString());
        Assert.Equal(
            1L,
            await CountAsync("select count(*) from bank_accounts where shop_id = @shop", first));

        using var read = new HttpRequestMessage(HttpMethod.Get, $"/api/shops/{first}");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
        var readBack = await client.SendAsync(read);

        Assert.Equal(HttpStatusCode.OK, readBack.StatusCode);
        var readBody = await readBack.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("State Bank of India", readBody.GetProperty("bankName").GetString());
        Assert.Equal("00112233445566", readBody.GetProperty("bankAccountNumber").GetString());
        // The elevation was transaction-local: the session is still scoped to the
        // second shop, which the same token can still read.
        using var ownScope = new HttpRequestMessage(HttpMethod.Get, $"/api/shops/{second}");
        ownScope.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ownScope)).StatusCode);
    }

    /// <summary>
    /// Editing a shop returns its bank details in the same response, and that read
    /// is tenant data for a route-supplied shop. Without a scope it comes back
    /// empty for any shop that is not the caller's active one — no error, HTTP 200,
    /// and every bank field silently null, which the client then caches.
    /// </summary>
    [Fact]
    public async Task OwnerUpdatesNonActiveShop_KeepsBankDetailsInResponse()
    {
        using var client = CreateClient();
        var (_, _, token) = await EnrollAsync(client, "update");
        var (first, firstToken) = await CreateShopAsync(client, token, "Update First");
        var (_, secondToken) = await CreateShopAsync(client, firstToken, "Update Second");

        using var bankDetails = new HttpRequestMessage(HttpMethod.Put, $"/api/shops/{first}/bank-details");
        bankDetails.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
        bankDetails.Content = JsonContent.Create(new
        {
            bankName = "Canara Bank",
            accountNumber = "77665544332211",
            accountType = "Savings",
            ifscCode = "CNRB0001234",
            accountHolderName = "Update First",
        });
        (await client.SendAsync(bankDetails)).EnsureSuccessStatusCode();

        // Scoped to the second shop, renaming the first.
        using var rename = new HttpRequestMessage(HttpMethod.Put, $"/api/shops/{first}");
        rename.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
        rename.Content = JsonContent.Create(new
        {
            name = "Update First Renamed",
            address = "9 Residency Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560025",
        });
        var response = await client.SendAsync(rename);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Update First Renamed", body.GetProperty("name").GetString());
        Assert.Equal("Canara Bank", body.GetProperty("bankName").GetString());
        Assert.Equal("77665544332211", body.GetProperty("bankAccountNumber").GetString());

        // And the row was never touched.
        Assert.Equal(
            1L,
            await CountAsync("select count(*) from bank_accounts where shop_id = @shop", first));
    }

    [Fact]
    public async Task NonOwner_CannotElevateIntoAnotherShop()
    {
        using var client = CreateClient();
        var (_, _, strangerToken) = await EnrollAsync(client, "stranger");
        var (targetShop, _) = await CreateShopAsync(client, strangerToken, "Target Shop");

        var (_, _, outsiderToken) = await EnrollAsync(client, "outsider");
        var (_, outsiderShopToken) = await CreateShopAsync(client, outsiderToken, "Outsider Shop");

        using var write = new HttpRequestMessage(HttpMethod.Put, $"/api/shops/{targetShop}/bank-details");
        write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outsiderShopToken);
        write.Content = JsonContent.Create(new
        {
            bankName = "Seized Bank",
            accountNumber = "99999999999999",
            accountType = "Current",
            ifscCode = "SBIN0009999",
            accountHolderName = "Outsider",
        });
        var written = await client.SendAsync(write);

        Assert.True(
            written.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"A non-member must not administer another shop, got {(int)written.StatusCode}.");
        Assert.Equal(0L, await CountAsync("select count(*) from bank_accounts where shop_id = @shop", targetShop));

        using var read = new HttpRequestMessage(HttpMethod.Get, $"/api/shops/{targetShop}");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", outsiderShopToken);
        var readBack = await client.SendAsync(read);

        Assert.True(
            readBack.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"A non-member must not read another shop's details, got {(int)readBack.StatusCode}.");
    }
}
