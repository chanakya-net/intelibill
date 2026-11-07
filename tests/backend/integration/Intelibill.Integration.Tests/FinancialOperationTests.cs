using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Operations;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Proves against PostgreSQL that a keyed customer or supplier payment has exactly one economic
/// effect however it is retried, raced or replayed, and that only an authorized member of the
/// same shop ever sees the committed result.
/// </summary>
[Collection("Integration Tests")]
public sealed class FinancialOperationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    [Fact]
    public async Task PurchaseOrderReceiptLookup_RecognizesSlugAndTreatsMissAsUnresolved()
    {
        using var client = CreateClient();
        var (_, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/financial-operations/purchase-order-receipt/{Guid.NewGuid():D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Operation.NotFound", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ConcurrentSameKey_CommitsOnce()
    {
        using var client = CreateClient();
        var (_, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await AddSupplierAsync(client, ownerToken);
        var key = $"supplier-pay-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            using var parallelClient = CreateClient();
            return await PostSupplierPaymentAsync(parallelClient, ownerToken, supplierId, 500m, key);
        })));

        var entryIds = new HashSet<Guid>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            entryIds.Add(body.GetProperty("id").GetGuid());
        }

        var entryId = Assert.Single(entryIds);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.OperationReceipts.CountAsync(r => r.OperationKey == key));
        Assert.Equal(1, await db.SupplierLedgerEntries.CountAsync(e => e.SupplierId == supplierId && e.EntryType == SupplierLedgerEntryType.PaymentMade));
        Assert.Equal(1, await db.Expenses.CountAsync(e => e.SupplierLedgerEntryId == entryId));
    }

    [Fact]
    public async Task SameKeyChangedAmount_Conflicts()
    {
        using var client = CreateClient();
        var (_, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await AddSupplierAsync(client, ownerToken);
        var key = $"supplier-pay-{Guid.NewGuid():N}";

        var first = await PostSupplierPaymentAsync(client, ownerToken, supplierId, 500m, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var changed = await PostSupplierPaymentAsync(client, ownerToken, supplierId, 501m, key);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var problem = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Operation.PayloadMismatch", problem.GetProperty("title").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entries = await db.SupplierLedgerEntries.Where(e => e.SupplierId == supplierId).ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(500m, entry.Amount);
        Assert.Equal(1, await db.Expenses.CountAsync(e => e.SupplierLedgerEntryId == entry.Id));
    }

    [Fact]
    public async Task LostResponse_ReplaysSavedResult()
    {
        using var client = CreateClient();
        var (_, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var customerId = await AddCustomerAsync(client, ownerToken);
        var key = $"customer-pay-{Guid.NewGuid():N}";

        // The first answer is "lost": the client only kept the key and the frozen payload.
        var original = await PostCustomerPaymentAsync(client, ownerToken, customerId, 500m, key);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        var originalBody = await original.Content.ReadFromJsonAsync<JsonElement>();

        // A later, unrelated payment moves the balance; the replay must not recompute it.
        var later = await PostCustomerPaymentAsync(client, ownerToken, customerId, 100m, $"customer-pay-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);

        var retry = await PostCustomerPaymentAsync(client, ownerToken, customerId, 500m, key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var retryBody = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(originalBody.GetProperty("entryId").GetGuid(), retryBody.GetProperty("entryId").GetGuid());
        Assert.Equal(originalBody.GetProperty("runningBalance").GetDecimal(), retryBody.GetProperty("runningBalance").GetDecimal());

        var lookup = await GetOperationAsync(client, ownerToken, "customer-payment", key);
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        var lookupBody = await lookup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(customerId, lookupBody.GetProperty("targetId").GetGuid());
        Assert.Equal(
            originalBody.GetProperty("entryId").GetGuid(),
            lookupBody.GetProperty("response").GetProperty("entryId").GetGuid());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.CustomerLedgerEntries.CountAsync(e => e.CustomerId == customerId));
    }

    [Fact]
    public async Task ActionFailure_RollsBackReservationAndPosting()
    {
        using var client = CreateClient();
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var customerId = await AddCustomerAsync(client, ownerToken);
        var key = $"customer-pay-{Guid.NewGuid():N}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<IFinancialOperationExecutor>();
            var ledger = scope.ServiceProvider.GetRequiredService<ICustomerLedgerEntryRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync<Guid>(
                new FinancialOperationScope(shopId, FinancialOperationType.CustomerPayment, key),
                Guid.NewGuid(),
                customerId,
                new string('a', 64),
                async ct =>
                {
                    var entry = CustomerLedgerEntry.Create(
                        shopId, customerId, null, CustomerLedgerEntryType.PaymentReceived,
                        500m, new DateOnly(2026, 11, 1), "crash", Guid.NewGuid()).Value;
                    await ledger.AddAsync(entry, ct);
                    await unitOfWork.SaveChangesAsync(ct);
                    throw new InvalidOperationException("Process died before commit.");
                },
                CancellationToken.None));
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.OperationReceipts.CountAsync(r => r.OperationKey == key));
            Assert.Equal(0, await db.CustomerLedgerEntries.CountAsync(e => e.CustomerId == customerId));
        }

        // Nothing was reserved, so the honest retry posts exactly once.
        var retry = await PostCustomerPaymentAsync(client, ownerToken, customerId, 500m, key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task ActionError_RollsBackReservation()
    {
        using var client = CreateClient();
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var customerId = await AddCustomerAsync(client, ownerToken);
        var key = $"customer-pay-{Guid.NewGuid():N}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<IFinancialOperationExecutor>();
            var result = await executor.ExecuteAsync<Guid>(
                new FinancialOperationScope(shopId, FinancialOperationType.CustomerPayment, key),
                Guid.NewGuid(),
                customerId,
                new string('a', 64),
                _ => Task.FromResult<ErrorOr<Guid>>(Error.Validation("Test.Rejected", "Rejected.")),
                CancellationToken.None);
            Assert.True(result.IsError);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.OperationReceipts.CountAsync(r => r.OperationKey == key));
        }
    }

    [Fact]
    public async Task UnauthorizedReplay_DoesNotDiscloseResult()
    {
        using var client = CreateClient();
        var (shopId, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var customerId = await AddCustomerAsync(client, ownerToken);
        var key = $"customer-pay-{Guid.NewGuid():N}";

        var original = await PostCustomerPaymentAsync(client, ownerToken, customerId, 500m, key);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        var entryId = (await original.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entryId").GetGuid().ToString();

        // A manager of the same shop may reconcile the owner's operation.
        var (_, managerEmail, managerPassword) = await AddShopUserAsync(client, ownerToken, shopId, "Manager");
        var managerToken = await LoginAsync(client, managerEmail, managerPassword);
        var managerLookup = await GetOperationAsync(client, managerToken, "customer-payment", key);
        Assert.Equal(HttpStatusCode.OK, managerLookup.StatusCode);
        var managerReplay = await PostCustomerPaymentAsync(client, managerToken, customerId, 500m, key);
        Assert.Equal(HttpStatusCode.OK, managerReplay.StatusCode);
        Assert.Contains(entryId, await managerReplay.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Staff may neither replay nor read it.
        var (_, staffEmail, staffPassword) = await AddShopUserAsync(client, ownerToken, shopId, "Staff");
        var staffToken = await LoginAsync(client, staffEmail, staffPassword);
        var staffReplay = await PostCustomerPaymentAsync(client, staffToken, customerId, 500m, key);
        Assert.Equal(HttpStatusCode.Forbidden, staffReplay.StatusCode);
        Assert.DoesNotContain(entryId, await staffReplay.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var staffLookup = await GetOperationAsync(client, staffToken, "customer-payment", key);
        Assert.Equal(HttpStatusCode.Forbidden, staffLookup.StatusCode);
        Assert.DoesNotContain(entryId, await staffLookup.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A manager whose membership is withdrawn is refused before any receipt is read.
        var (removedUserId, removedEmail, removedPassword) = await AddShopUserAsync(client, ownerToken, shopId, "Manager");
        var removedToken = await LoginAsync(client, removedEmail, removedPassword);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ShopMemberships.RemoveRange(await db.ShopMemberships
                .Where(sm => sm.UserId == removedUserId && sm.ShopId == shopId)
                .ToListAsync());
            await db.SaveChangesAsync();
        }

        var removedReplay = await PostCustomerPaymentAsync(client, removedToken, customerId, 500m, key);
        Assert.Contains(removedReplay.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        Assert.DoesNotContain(entryId, await removedReplay.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var removedLookup = await GetOperationAsync(client, removedToken, "customer-payment", key);
        Assert.Contains(removedLookup.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound });

        // Another shop's owner sees nothing under the same key.
        var (_, otherOwnerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var otherLookup = await GetOperationAsync(client, otherOwnerToken, "customer-payment", key);
        Assert.Equal(HttpStatusCode.NotFound, otherLookup.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.CustomerLedgerEntries.CountAsync(e => e.CustomerId == customerId));
        }
    }

    [Fact]
    public async Task ScopeIsolation_AllowsSameKeyInOtherShop()
    {
        using var client = CreateClient();
        var (_, firstOwnerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var (_, secondOwnerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var firstSupplierId = await AddSupplierAsync(client, firstOwnerToken);
        var secondSupplierId = await AddSupplierAsync(client, secondOwnerToken);
        var key = $"shared-key-{Guid.NewGuid():N}";

        var first = await PostSupplierPaymentAsync(client, firstOwnerToken, firstSupplierId, 500m, key);
        var second = await PostSupplierPaymentAsync(client, secondOwnerToken, secondSupplierId, 700m, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.NotEqual(firstId, secondId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.OperationReceipts.CountAsync(r => r.OperationKey == key));
        Assert.Equal(700m, (await db.SupplierLedgerEntries.SingleAsync(e => e.Id == secondId)).Amount);
    }

    [Fact]
    public async Task UnknownKey_ReturnsNotFound()
    {
        using var client = CreateClient();
        var (_, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));

        var response = await GetOperationAsync(client, ownerToken, "supplier-payment", $"never-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Operation.NotFound", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnkeyedPayment_StillPostsForCompatibility()
    {
        using var client = CreateClient();
        var (_, ownerToken) = await CreateShopAsync(client, await RegisterAsync(client));
        var supplierId = await AddSupplierAsync(client, ownerToken);

        var response = await PostSupplierPaymentAsync(client, ownerToken, supplierId, 250m, operationKey: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.SupplierLedgerEntries.CountAsync(e => e.SupplierId == supplierId));
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.EnrollAsync(new
        {
            email = $"financial-ops-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Test",
            lastName = "User",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
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

    private static async Task<(Guid UserId, string Email, string Password)> AddShopUserAsync(
        HttpClient client, string ownerToken, Guid shopId, string role)
    {
        var email = $"member-{Guid.NewGuid():N}@test.com";
        const string password = "Pass123!Aa";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            shopIds = new[] { shopId },
            email,
            firstName = "Shop",
            lastName = "Member",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
            password,
            confirmPassword = password,
            role,
        });

        var response = await client.SeedVerifiedStaffAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("userId").GetGuid(), email, password);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login/email", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static async Task<Guid> AddSupplierAsync(HttpClient client, string ownerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            name = $"Supplier {Guid.NewGuid():N}",
            contactPersonName = "Ramesh",
            contactPersonPhone = "+919999999999",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            isActive = true,
            isPreferred = false,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("supplierId").GetGuid();
    }

    private static async Task<Guid> AddCustomerAsync(HttpClient client, string ownerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        request.Content = JsonContent.Create(new
        {
            name = "Asha",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
            address = (string?)null,
            isActive = true,
            creditLimit = 0m,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("customerId").GetGuid();
    }

    private static async Task<HttpResponseMessage> PostSupplierPaymentAsync(
        HttpClient client, string token, Guid supplierId, decimal amount, string? operationKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/suppliers/{supplierId}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { amount, paymentDate = "2026-11-01", notes = "NEFT", operationKey });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostCustomerPaymentAsync(
        HttpClient client, string token, Guid customerId, decimal amount, string operationKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/customers/{customerId}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { amount, paymentDate = "2026-11-01", notes = "cash", operationKey });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetOperationAsync(HttpClient client, string token, string type, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/financial-operations/{type}/{Uri.EscapeDataString(key)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
