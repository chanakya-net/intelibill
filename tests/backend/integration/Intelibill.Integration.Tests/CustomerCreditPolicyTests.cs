using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Customer credit policy against PostgreSQL: the saved limit is what the account shows, due is
/// judged under the customer's row lock so concurrent sales cannot both take the last of the limit,
/// and the account statement's opening plus signed entries always equals its closing.
/// </summary>
[Collection("Integration Tests")]
public sealed class CustomerCreditPolicyTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
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

    [Fact]
    public async Task Account_shows_the_saved_limit_and_enforced_policy()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop, 2000m, "Enforced", termsDays: 15);
        await SellServiceAsync(shop, customerId, price: 500m, due: 500m);

        var account = await GetAccountAsync(shop, customerId);

        Assert.Equal(2000m, account.GetProperty("creditLimit").GetDecimal());
        Assert.Equal("Enforced", account.GetProperty("creditPolicyMode").GetString());
        Assert.Equal(1500m, account.GetProperty("availableCredit").GetDecimal());
        Assert.Equal(0m, account.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(500m, account.GetProperty("closingBalance").GetDecimal());
        var sale = account.GetProperty("sales")[0];
        Assert.Equal(JsonValueKind.String, sale.GetProperty("dueDate").ValueKind);
        Assert.False(sale.GetProperty("isOverdue").GetBoolean());
        Assert.Equal(0m, account.GetProperty("overdueAmount").GetDecimal());
    }

    [Fact]
    public async Task Existing_style_customer_without_policy_is_legacy_advisory_and_unblocked()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop, 100m, policy: null);

        using var response = await SellServiceAsync(shop, customerId, price: 300m, due: 300m, expectCreated: false);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await GetAccountAsync(shop, customerId);
        Assert.Equal("LegacyAdvisory", account.GetProperty("creditPolicyMode").GetString());
        Assert.Equal(JsonValueKind.Null, account.GetProperty("availableCredit").ValueKind);
    }

    [Fact]
    public async Task Concurrent_due_sales_never_exceed_the_enforced_limit()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop, 100m, "Enforced");
        var batch = await ReceiveStockAsync(shop, quantity: 20m, salesPrice: 10m);
        using (var opening = await SellGoodsAsync(shop, customerId, batch, quantity: 9m))
            Assert.Equal(HttpStatusCode.Created, opening.StatusCode);

        using var second = await shop.CloneAsync(_factory);
        var responses = await Task.WhenAll(
            SellGoodsAsync(shop, customerId, batch, quantity: 1m),
            SellGoodsAsync(second, customerId, batch, quantity: 1m),
            SellGoodsAsync(shop, customerId, batch, quantity: 1m));

        try
        {
            Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            {
                Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
                Assert.Contains("Customer.CreditLimitExceeded", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        var account = await GetAccountAsync(shop, customerId);
        Assert.Equal(100m, account.GetProperty("outstandingDue").GetDecimal());
        Assert.Equal(2, account.GetProperty("sales").GetArrayLength());
        Assert.Equal(2, await QueryAsync(db => db.StockTransactions.CountAsync(t => t.InventoryBatchId == batch.BatchId && t.TransactionType == StockTransactionType.Out)));
        Assert.Equal(10m, await QueryAsync(db => db.InventoryBatches.Where(b => b.Id == batch.BatchId).Select(b => b.Quantity).SingleAsync()));
    }

    [Fact]
    public async Task Inactive_and_no_credit_customers_cannot_take_due_but_can_pay_in_full()
    {
        using var shop = await OpenShopAsync();
        var noCredit = await AddCustomerAsync(shop, 0m, "NoCredit");
        var inactive = await AddCustomerAsync(shop, 500m, "Enforced", isActive: false);

        using var noCreditDue = await SellServiceAsync(shop, noCredit, price: 50m, due: 50m, expectCreated: false);
        using var inactiveDue = await SellServiceAsync(shop, inactive, price: 50m, due: 50m, expectCreated: false);
        using var paidInFull = await SellServiceAsync(shop, noCredit, price: 50m, due: 0m, expectCreated: false);

        await AssertRefusedAsync(noCreditDue, HttpStatusCode.Conflict, "Customer.CreditNotExtended");
        await AssertRefusedAsync(inactiveDue, HttpStatusCode.Conflict, "Customer.CreditCustomerInactive");
        Assert.Equal(HttpStatusCode.Created, paidInFull.StatusCode);
        Assert.Equal(0, await QueryAsync(db => db.CustomerLedgerEntries.CountAsync(e => e.CustomerId == noCredit || e.CustomerId == inactive)));
    }

    [Fact]
    public async Task Owner_override_is_audited_and_a_stale_override_is_refused()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop, 100m, "Enforced");
        await SellServiceAsync(shop, customerId, price: 80m, due: 80m);

        using var refused = await SellServiceAsync(shop, customerId, price: 50m, due: 50m, expectCreated: false);
        await AssertRefusedAsync(refused, HttpStatusCode.Conflict, "Customer.CreditLimitExceeded");

        var credit = await ReadAsync(await SendAsync(shop, HttpMethod.Get, $"/api/customers/{customerId}/credit"));
        Assert.Equal(80m, credit.GetProperty("currentExposure").GetDecimal());
        Assert.Equal(20m, credit.GetProperty("availableCredit").GetDecimal());

        using var stale = await SellServiceAsync(shop, customerId, price: 50m, due: 50m, expectCreated: false,
            creditOverride: new { reason = "Seen earlier", observedExposure = 10m });
        await AssertRefusedAsync(stale, HttpStatusCode.Conflict, "Customer.CreditOverrideStale");

        var saleId = await SellServiceAsync(shop, customerId, price: 50m, due: 50m,
            creditOverride: new { reason = "Wedding order, pays Friday", observedExposure = 80m });

        var audit = await QueryAsync(db => db.Sales.Where(s => s.Id == saleId)
            .Select(s => new { s.CreditOverrideReason, s.CreditOverrideByUserId, s.CreditOverrideObservedExposure, s.CreditOverrideLimit })
            .SingleAsync());
        Assert.Equal("Wedding order, pays Friday", audit.CreditOverrideReason);
        Assert.NotNull(audit.CreditOverrideByUserId);
        Assert.Equal(80m, audit.CreditOverrideObservedExposure);
        Assert.Equal(100m, audit.CreditOverrideLimit);
        Assert.Equal(130m, (await GetAccountAsync(shop, customerId)).GetProperty("outstandingDue").GetDecimal());
    }

    [Fact]
    public async Task Collections_returns_and_reversals_keep_the_account_equation()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop, 5000m, "Enforced");
        await SellServiceAsync(shop, customerId, price: 500m, due: 500m);
        var later = await SellServiceAsync(shop, customerId, price: 100m, due: 100m);
        var returned = await SellServiceAsync(shop, customerId, price: 50m, due: 50m);
        var payment = await ReadAsync(await SendAsync(shop, HttpMethod.Post, $"/api/customers/{customerId}/payments", new
        {
            amount = 200m,
            paymentDate = Today,
            notes = "Collected",
            operationKey = $"pay-{Guid.NewGuid():N}",
            method = "Cash",
            allocations = new[] { new { targetId = later, amount = 100m } },
        }));
        using (var returnResponse = await ReturnServiceAsync(shop, returned, 50m))
            Assert.True(returnResponse.IsSuccessStatusCode, await returnResponse.Content.ReadAsStringAsync());
        var receiptId = payment.GetProperty("receiptId").GetGuid();
        using (var reversal = await SendAsync(shop, HttpMethod.Post, $"/api/payments/{receiptId}/reversal", new { reason = "Cheque bounced", operationKey = $"rev-{Guid.NewGuid():N}" }))
            Assert.True(reversal.IsSuccessStatusCode, await reversal.Content.ReadAsStringAsync());

        // 500 + 100 + 50 − 200 − 50 + 200 = 600 on the whole account.
        var account = await GetAccountAsync(shop, customerId);
        AssertStatementReconciles(account);
        Assert.Equal(600m, account.GetProperty("closingBalance").GetDecimal());
        Assert.Equal(600m, account.GetProperty("outstandingDue").GetDecimal());

        // A window after every entry opens with all of them and has nothing in it.
        var tomorrow = BusinessDates.Today.AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var window = await ReadAsync(await SendAsync(shop, HttpMethod.Get, $"/api/customers/{customerId}/account?from={tomorrow}"));
        Assert.Equal(600m, window.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(600m, window.GetProperty("closingBalance").GetDecimal());
        Assert.Equal(0, window.GetProperty("ledgerEntries").GetArrayLength());
    }

    private static void AssertStatementReconciles(JsonElement account)
    {
        var signed = account.GetProperty("ledgerEntries").EnumerateArray().Sum(e => e.GetProperty("signedAmount").GetDecimal());
        Assert.Equal(
            account.GetProperty("closingBalance").GetDecimal(),
            account.GetProperty("openingBalance").GetDecimal() + signed);
    }

    // ── Seeding and requests ──────────────────────────────────────────────────

    private static string Today => BusinessDates.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record StockBatch(Guid BatchId, string Barcode, string BatchNumber);

    private sealed class Shop(HttpClient client, string token) : IDisposable
    {
        public HttpClient Client { get; } = client;
        public string Token { get; } = token;

        public Task<Shop> CloneAsync(ApiWebApplicationFactory factory) =>
            Task.FromResult(new Shop(factory.CreateClient(ClientOptions), Token));

        public void Dispose() => Client.Dispose();
    }

    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    };

    private async Task<Shop> OpenShopAsync()
    {
        var client = _factory.CreateClient(ClientOptions);
        var enrolled = await client.EnrollAsync(new
        {
            email = $"credit-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Credit",
            lastName = "Owner",
        });
        enrolled.EnsureSuccessStatusCode();
        var token = (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
            gstNumber = TestTaxProfile.NewGstin(),
            taxProfile = TestTaxProfile.Regular,
        });
        using var created = await client.SendAsync(request);
        created.EnsureSuccessStatusCode();
        var shopToken = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        return new Shop(client, shopToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(Shop shop, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", shop.Token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await shop.Client.SendAsync(request);
    }

    private static async Task<Guid> AddCustomerAsync(Shop shop, decimal creditLimit, string? policy, int? termsDays = null, bool isActive = true)
    {
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/customers", new
        {
            name = "Credit Customer",
            phoneNumber = $"+9198{Random.Shared.Next(10_000_000, 99_999_999)}",
            address = (string?)null,
            isActive,
            creditLimit,
            creditPolicyMode = policy,
            creditTermsDays = termsDays,
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("customerId").GetGuid();
    }

    private static async Task<StockBatch> ReceiveStockAsync(Shop shop, decimal quantity, decimal salesPrice)
    {
        var barcode = $"CRD-{Guid.NewGuid():N}"[..20];
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/inventory/inbound", new
        {
            itemName = "Credit Item",
            barcode,
            itemDescription = (string?)null,
            hsnCode = (string?)null,
            uom = "pcs",
            batchNumber = "B-001",
            quantity,
            totalPurchaseCost = quantity * salesPrice / 2m,
            mrp = salesPrice,
            salesPrice,
            taxRatePercent = 0m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId = (Guid?)null,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        var batchId = JsonSerializer.Deserialize<JsonElement>(content).GetProperty("inventoryBatchId").GetGuid();
        return new StockBatch(batchId, barcode, "B-001");
    }

    private static Task<HttpResponseMessage> SellGoodsAsync(Shop shop, Guid customerId, StockBatch batch, decimal quantity) =>
        SendAsync(shop, HttpMethod.Post, "/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId,
            customerName = "Credit Customer",
            customerPhone = (string?)null,
            paymentMethod = (int)PaymentMethod.Credit,
            paidAmount = 0m,
            dueAmount = quantity * 10m,
            items = new[]
            {
                new
                {
                    barcode = batch.Barcode,
                    batchNumber = batch.BatchNumber,
                    itemName = "Credit Item",
                    quantity,
                    costPrice = 5m,
                    salesPrice = 10m,
                    mrp = 10m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batch.BatchId,
                },
            },
        });

    private static async Task<Guid> AddServiceAsync(Shop shop, decimal price)
    {
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/services", new
        {
            name = $"Service {Guid.NewGuid():N}"[..20],
            description = "Credit test service",
            price,
            hsnCode = "9987",
            taxRatePercent = 0m,
            taxIncluded = false,
            isActive = true,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("serviceId").GetGuid();
    }

    private static async Task<Guid> SellServiceAsync(Shop shop, Guid customerId, decimal price, decimal due, object? creditOverride = null)
    {
        using var response = await SellServiceAsync(shop, customerId, price, due, expectCreated: true, creditOverride);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, content);
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("saleId").GetGuid();
    }

    private static async Task<HttpResponseMessage> SellServiceAsync(Shop shop, Guid customerId, decimal price, decimal due, bool expectCreated, object? creditOverride = null)
    {
        _ = expectCreated;
        var serviceId = await AddServiceAsync(shop, price);
        return await SendAsync(shop, HttpMethod.Post, "/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId,
            customerName = "Credit Customer",
            customerPhone = (string?)null,
            paymentMethod = (int)(due > 0m ? PaymentMethod.Credit : PaymentMethod.Cash),
            paidAmount = price - due,
            dueAmount = due,
            items = new[]
            {
                new
                {
                    barcode = "SVC-CRD",
                    batchNumber = string.Empty,
                    itemName = "Credit Service",
                    quantity = 1m,
                    costPrice = 0m,
                    salesPrice = price,
                    mrp = 0m,
                    taxRatePercent = 0m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = Guid.Empty,
                    lineType = "Service",
                    serviceId,
                },
            },
            creditOverride,
        });
    }

    private async Task<HttpResponseMessage> ReturnServiceAsync(Shop shop, Guid saleId, decimal amount)
    {
        var lineId = await QueryAsync(db => db.SaleItems.Where(i => i.SaleId == saleId).Select(i => i.Id).SingleAsync());
        return await SendAsync(shop, HttpMethod.Post, $"/api/sales/{saleId}/returns", new
        {
            payoutDestination = (int)ReturnPayoutDestination.Refund,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Credit return",
            items = new[]
            {
                new
                {
                    saleItemId = lineId,
                    quantity = 1m,
                    lineType = (int)SaleLineType.Service,
                    condition = (int?)null,
                    approvedRefundAmount = amount,
                    notes = (string?)null,
                },
            },
        });
    }

    private static async Task<JsonElement> GetAccountAsync(Shop shop, Guid customerId)
    {
        using var response = await SendAsync(shop, HttpMethod.Get, $"/api/customers/{customerId}/account");
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {content}");
        return JsonSerializer.Deserialize<JsonElement>(content);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {content}");
        Assert.Contains(code, content, StringComparison.Ordinal);
    }

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
