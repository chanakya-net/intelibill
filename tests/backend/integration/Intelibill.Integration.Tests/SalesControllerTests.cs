using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class SalesControllerTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
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

    private static string UniqueEmail() => $"sales-{Guid.NewGuid():N}@test.com";
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

    private static async Task<JsonElement> AddInventoryAsync(
        HttpClient client, string token, string barcode, string batchNumber, decimal quantity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            itemName = "Test Item",
            barcode,
            itemDescription = (string?)null,
            hsnCode = (string?)null,
            uom = "kg",
            batchNumber,
            quantity,
            totalPurchaseCost = 80m * quantity,
            mrp = 120m,
            salesPrice = 100m,
            taxRatePercent = 18m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
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

    private static async Task<Guid> AddServiceAsync(HttpClient client, string token, string name, decimal price = 100m)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/services");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name,
            description = "Test service",
            price,
            hsnCode = "9987",
            taxRatePercent = 18m,
            taxIncluded = false,
            isActive = true,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("serviceId").GetGuid();
    }

    private static async Task AddInventoryBatchAsync(HttpClient client, string token, int count)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/inbound/batch");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var items = Enumerable.Range(0, count)
            .Select(i => new
            {
                clientRowId = $"row-{i}",
                itemName = $"Bulk Item {i}",
                barcode = $"BULK-{Guid.NewGuid():N}",
                itemDescription = (string?)null,
                uom = "kg",
                batchNumber = $"BATCH-{i:000}",
                quantity = 1m,
                totalPurchaseCost = 80m,
                mrp = 120m,
                salesPrice = 100m,
                taxRatePercent = 18m,
                taxIncluded = false,
                purchaseTaxIncluded = false,
                expiryDate = (DateOnly?)null,
                manufacturingDate = (DateOnly?)null,
                supplierId = (Guid?)null,
                referenceNumber = (string?)null,
                notes = (string?)null,
                performedAt = (DateTimeOffset?)null,
                hsnCode = (string?)null,
            })
            .ToList();

        request.Content = JsonContent.Create(new { items });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task VoidBatchDirectlyAsync(string ownerToken, Guid batchId)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(ownerToken);
        var userId = Guid.Parse(jwt.Subject);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var batch = await db.InventoryBatches.SingleAsync(b => b.Id == batchId);
        batch.Void(userId);
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddCustomerAsync(HttpClient client, string token, string name, string phoneNumber, bool isActive)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name,
            phoneNumber,
            address = (string?)null,
            isActive,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("customerId").GetGuid();
    }

    private static async Task<Guid> CreateDiscountRuleAsync(HttpClient client, string token, Guid? inventoryBatchId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/discounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            ruleType = (inventoryBatchId is null ? DiscountRuleType.SalePercentage : DiscountRuleType.BatchPercentage).ToString(),
            name = "Offline 5% Rule",
            description = (string?)null,
            inventoryBatchId,
            percentage = 5m,
            thresholdAmount = (decimal?)null,
            startsAt = (DateTimeOffset?)null,
            endsAt = (DateTimeOffset?)null,
            belowCostConfirmed = true,
            belowCostConfirmationReason = "Test",
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static async Task DisableDiscountRuleAsync(HttpClient client, string token, Guid ruleId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/discounts/{ruleId}/disable");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { reason = "Disabled for test" });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> ReserveInvoiceLeaseAsync(HttpClient client, string token, string deviceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sales/invoice-leases/reserve");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            deviceId,
            blockSize = 25,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string FormatInvoiceNumber(string prefix, int number, int padding) =>
        $"{prefix}{number.ToString($"D{padding}", System.Globalization.CultureInfo.InvariantCulture)}";

    private static async Task<Guid> SyncOfflineSaleAsync(
        HttpClient client,
        string token,
        string deviceId,
        string invoiceNumber,
        DateTimeOffset soldAt,
        string barcode,
        Guid batchId)
    {
        using var syncRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales/offline-sync");
        syncRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        syncRequest.Content = JsonContent.Create(new
        {
            deviceId,
            sales = new[]
            {
                new
                {
                    clientSaleId = $"offline-{Guid.NewGuid():N}",
                    invoiceNumber,
                    soldAt,
                    paymentMethod = (int)PaymentMethod.Cash,
                    paidAmount = 118m,
                    dueAmount = 0m,
                    subtotalBeforeDiscount = 100m,
                    totalBeforeDiscount = 118m,
                    totalDiscountAmount = 0m,
                    totalTaxAmount = 18m,
                    totalAmount = 118m,
                    saleDiscountOverrideType = (int)InstantDiscountType.None,
                    saleDiscountOverrideValue = 0m,
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
                            preTaxAmountBeforeDiscount = 100m,
                            itemDiscountAmount = 0m,
                            saleDiscountAmount = 0m,
                            taxableAmount = 100m,
                            taxAmount = 18m,
                            totalAmount = 118m,
                            itemDiscountOverrideType = (int)InstantDiscountType.None,
                            itemDiscountOverrideValue = 0m,
                        },
                    },
                },
            },
        });

        var response = await client.SendAsync(syncRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = body.GetProperty("results").EnumerateArray().Single();
        return result.GetProperty("saleId").GetGuid();
    }

    private async Task RecordSimpleReturnAsync(HttpClient client, string token, Guid saleId, decimal refundAmount)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Return one unit restockable",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = refundAmount,
                    notes = (string?)null,
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);
    }

    private static async Task<(Guid UserId, string Email, string Password)> AddShopUserAsync(HttpClient client, string ownerToken, Guid shopId)
    {
        var email = $"member-{Guid.NewGuid():N}@test.com";
        var password = "Pass123!Aa";

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
            role = "Staff",
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("userId").GetGuid(), email, password);
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadNdjsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var lines = new List<JsonElement>();
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using var doc = JsonDocument.Parse(line);
            lines.Add(doc.RootElement.Clone());
        }

        return lines;
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
    public async Task StreamOfflineSnapshot_AsOwner_ReturnsNdjson_EndsWithComplete_AndFiltersInactive()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var shopId = await GetShopIdFromTokenAsync(client, ownerToken);

        var barcode = UniqueBarcode();
        var batch1 = await AddInventoryAsync(client, ownerToken, barcode, "BATCH-001", 10m);
        var batch1Id = batch1.GetProperty("inventoryBatchId").GetGuid();

        var batch2 = await AddInventoryAsync(client, ownerToken, barcode, "BATCH-002", 5m);
        var batch2Id = batch2.GetProperty("inventoryBatchId").GetGuid();
        await VoidBatchDirectlyAsync(ownerToken, batch2Id);

        await AddCustomerAsync(client, ownerToken, "Active Customer", $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}", true);
        await AddCustomerAsync(client, ownerToken, "Inactive Customer", $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}", false);

        await CreateDiscountRuleAsync(client, ownerToken, inventoryBatchId: batch1Id);
        var disabledRuleId = await CreateDiscountRuleAsync(client, ownerToken);
        await DisableDiscountRuleAsync(client, ownerToken, disabledRuleId);

        await ReserveInvoiceLeaseAsync(client, ownerToken, deviceId: "device-1");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/offline-snapshot/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        var lines = await ReadNdjsonAsync(response);
        Assert.True(lines.Count > 0);

        var first = lines[0];
        Assert.Equal("metadata", first.GetProperty("type").GetString());
        var snapshotId = first.GetProperty("metadata").GetProperty("snapshotId").GetGuid();
        Assert.NotEqual(Guid.Empty, snapshotId);
        Assert.Equal(shopId, first.GetProperty("metadata").GetProperty("shopId").GetGuid());

        Assert.Equal("complete", lines[^1].GetProperty("type").GetString());
        Assert.Equal(snapshotId, lines[^1].GetProperty("complete").GetProperty("snapshotId").GetGuid());

        var batchIds = lines
            .Where(l => l.GetProperty("type").GetString() == "batch")
            .Select(l => l.GetProperty("batch").GetProperty("batchId").GetGuid())
            .ToList();

        Assert.Contains(batch1Id, batchIds);
        Assert.DoesNotContain(batch2Id, batchIds);

        var customerNames = lines
            .Where(l => l.GetProperty("type").GetString() == "customer")
            .Select(l => l.GetProperty("customer").GetProperty("name").GetString())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        Assert.Contains("Active Customer", customerNames);
        Assert.DoesNotContain("Inactive Customer", customerNames);

        Assert.DoesNotContain(lines, l => l.GetProperty("type").GetString() == "error");
    }

    [Fact]
    public async Task StreamOfflineSnapshot_WhenMembershipRemoved_Returns403()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var shopId = await GetShopIdFromTokenAsync(client, ownerToken);

        var (memberUserId, email, password) = await AddShopUserAsync(client, ownerToken, shopId);
        var memberToken = await LoginAsync(client, email, password);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var memberships = await db.ShopMemberships
                .Where(sm => sm.UserId == memberUserId && sm.ShopId == shopId)
                .ToListAsync();
            db.ShopMemberships.RemoveRange(memberships);
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/offline-snapshot/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StreamOfflineSnapshot_LargeShop_FlushesMetadataEarly()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        await AddInventoryBatchAsync(client, ownerToken, 60);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/offline-snapshot/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var firstLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(string.IsNullOrWhiteSpace(firstLine));
        using var doc = JsonDocument.Parse(firstLine!);
        Assert.Equal("metadata", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task OfflineSync_HappyPath_CreatesSaleWithFrozenValues()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var leaseBody = await ReserveInvoiceLeaseAsync(client, ownerToken, deviceId: "device-1");
        var prefix = leaseBody.GetProperty("prefix").GetString()!;
        var padding = leaseBody.GetProperty("numberPadding").GetInt32();
        var startNumber = leaseBody.GetProperty("rangeStart").GetInt32();
        var invoiceNumber = FormatInvoiceNumber(prefix, startNumber, padding);

        var soldAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var clientSaleId = $"offline-{Guid.NewGuid():N}";

        using var syncRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales/offline-sync");
        syncRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        syncRequest.Content = JsonContent.Create(new
        {
            deviceId = "device-1",
            sales = new[]
            {
                new
                {
                    clientSaleId,
                    invoiceNumber,
                    soldAt,
                    paymentMethod = (int)PaymentMethod.Cash,
                    paidAmount = 236m,
                    dueAmount = 0m,
                    subtotalBeforeDiscount = 200m,
                    totalBeforeDiscount = 236m,
                    totalDiscountAmount = 0m,
                    totalTaxAmount = 36m,
                    totalAmount = 236m,
                    saleDiscountOverrideType = (int)InstantDiscountType.None,
                    saleDiscountOverrideValue = 0m,
                    items = new[]
                    {
                        new
                        {
                            barcode,
                            batchNumber = "B-001",
                            itemName = "Test Item",
                            quantity = 2m,
                            costPrice = 80m,
                            salesPrice = 100m,
                            mrp = 120m,
                            taxRatePercent = 18m,
                            isPriceIncludingTax = false,
                            inventoryBatchId = batchId,
                            preTaxAmountBeforeDiscount = 200m,
                            itemDiscountAmount = 0m,
                            saleDiscountAmount = 0m,
                            taxableAmount = 200m,
                            taxAmount = 36m,
                            totalAmount = 236m,
                            itemDiscountOverrideType = (int)InstantDiscountType.None,
                            itemDiscountOverrideValue = 0m,
                        },
                    },
                },
            },
        });

        var response = await client.SendAsync(syncRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = body.GetProperty("results").EnumerateArray().Single();
        Assert.Equal(clientSaleId, result.GetProperty("clientSaleId").GetString());
        Assert.Equal("created", result.GetProperty("status").GetString());
        var saleId = result.GetProperty("saleId").GetGuid();
        Assert.NotEqual(Guid.Empty, saleId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == saleId);
        Assert.NotNull(sale);
        Assert.Equal(invoiceNumber, sale!.InvoiceNumber);
        Assert.Equal(236m, sale.TotalAmount);
        Assert.Equal(SaleSource.Offline, sale.Source);
        Assert.Equal("device-1", sale.DeviceId);
        Assert.Equal(clientSaleId, sale.ClientSaleId);
        Assert.NotNull(sale.SyncedAt);
        Assert.Single(sale.Items);
        Assert.Equal(236m, sale.Items[0].TotalAmount);

        var batch = await db.InventoryBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        Assert.NotNull(batch);
        Assert.Equal(8m, batch!.Quantity);

        var inventory = await db.Inventory.FirstOrDefaultAsync(i => i.ItemId == itemId);
        Assert.NotNull(inventory);
        Assert.Equal(8m, inventory!.Quantity);
    }

    [Fact]
    public async Task OfflineSync_DuplicateRequest_ReturnsExistingSale()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 5m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var leaseBody = await ReserveInvoiceLeaseAsync(client, ownerToken, deviceId: "device-1");
        var prefix = leaseBody.GetProperty("prefix").GetString()!;
        var padding = leaseBody.GetProperty("numberPadding").GetInt32();
        var startNumber = leaseBody.GetProperty("rangeStart").GetInt32();
        var invoiceNumber = FormatInvoiceNumber(prefix, startNumber, padding);

        var clientSaleId = $"offline-{Guid.NewGuid():N}";
        var soldAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var payload = new
        {
            deviceId = "device-1",
            sales = new[]
            {
                new
                {
                    clientSaleId,
                    invoiceNumber,
                    soldAt,
                    paymentMethod = (int)PaymentMethod.Cash,
                    paidAmount = 118m,
                    dueAmount = 0m,
                    subtotalBeforeDiscount = 100m,
                    totalBeforeDiscount = 118m,
                    totalDiscountAmount = 0m,
                    totalTaxAmount = 18m,
                    totalAmount = 118m,
                    saleDiscountOverrideType = (int)InstantDiscountType.None,
                    saleDiscountOverrideValue = 0m,
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
                            preTaxAmountBeforeDiscount = 100m,
                            itemDiscountAmount = 0m,
                            saleDiscountAmount = 0m,
                            taxableAmount = 100m,
                            taxAmount = 18m,
                            totalAmount = 118m,
                            itemDiscountOverrideType = (int)InstantDiscountType.None,
                            itemDiscountOverrideValue = 0m,
                        },
                    },
                },
            },
        };

        using var syncRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales/offline-sync");
        syncRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        syncRequest.Content = JsonContent.Create(payload);

        var firstResponse = await client.SendAsync(syncRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstSaleId = firstBody.GetProperty("results").EnumerateArray().Single().GetProperty("saleId").GetGuid();

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales/offline-sync");
        replayRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        replayRequest.Content = JsonContent.Create(payload);

        var replayResponse = await client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replayBody = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        var replayResult = replayBody.GetProperty("results").EnumerateArray().Single();
        Assert.Equal(firstSaleId, replayResult.GetProperty("saleId").GetGuid());
        Assert.Equal("duplicate", replayResult.GetProperty("status").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var saleCount = await db.Sales.CountAsync(s => s.ClientSaleId == clientSaleId);
        Assert.Equal(1, saleCount);

        var batch = await db.InventoryBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        Assert.NotNull(batch);
        Assert.Equal(4m, batch!.Quantity);
    }

    [Fact]
    public async Task OfflineSync_WhenMembershipMissing_ReturnsForbidden()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var shopId = await GetShopIdFromTokenAsync(client, ownerToken);

        var (memberUserId, email, password) = await AddShopUserAsync(client, ownerToken, shopId);
        var memberToken = await LoginAsync(client, email, password);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var memberships = await db.ShopMemberships
                .Where(sm => sm.UserId == memberUserId && sm.ShopId == shopId)
                .ToListAsync();
            db.ShopMemberships.RemoveRange(memberships);
            await db.SaveChangesAsync();
        }

        using var syncRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales/offline-sync");
        syncRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        syncRequest.Content = JsonContent.Create(new
        {
            deviceId = "device-1",
            sales = new[]
            {
                new
                {
                    clientSaleId = $"offline-{Guid.NewGuid():N}",
                    invoiceNumber = "INV-TEST-0001",
                    soldAt = DateTimeOffset.UtcNow,
                    paymentMethod = (int)PaymentMethod.Cash,
                    paidAmount = 118m,
                    dueAmount = 0m,
                    subtotalBeforeDiscount = 100m,
                    totalBeforeDiscount = 118m,
                    totalDiscountAmount = 0m,
                    totalTaxAmount = 18m,
                    totalAmount = 118m,
                    saleDiscountOverrideType = (int)InstantDiscountType.None,
                    saleDiscountOverrideValue = 0m,
                    items = new[]
                    {
                        new
                        {
                            barcode = "BC-001",
                            batchNumber = "B-001",
                            itemName = "Test Item",
                            quantity = 1m,
                            costPrice = 80m,
                            salesPrice = 100m,
                            mrp = 120m,
                            taxRatePercent = 18m,
                            isPriceIncludingTax = false,
                            inventoryBatchId = Guid.NewGuid(),
                            preTaxAmountBeforeDiscount = 100m,
                            itemDiscountAmount = 0m,
                            saleDiscountAmount = 0m,
                            taxableAmount = 100m,
                            taxAmount = 18m,
                            totalAmount = 118m,
                            itemDiscountOverrideType = (int)InstantDiscountType.None,
                            itemDiscountOverrideValue = 0m,
                        },
                    },
                },
            },
        });

        var response = await client.SendAsync(syncRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OfflineSync_WhenBatchLimitExceeded_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var sales = Enumerable.Range(0, 51)
            .Select(i => new
            {
                clientSaleId = $"offline-{i}-{Guid.NewGuid():N}",
                invoiceNumber = $"INV-TEST-{i:0000}",
                soldAt = DateTimeOffset.UtcNow,
                paymentMethod = (int)PaymentMethod.Cash,
                paidAmount = 118m,
                dueAmount = 0m,
                subtotalBeforeDiscount = 100m,
                totalBeforeDiscount = 118m,
                totalDiscountAmount = 0m,
                totalTaxAmount = 18m,
                totalAmount = 118m,
                saleDiscountOverrideType = (int)InstantDiscountType.None,
                saleDiscountOverrideValue = 0m,
                items = new[]
                {
                    new
                    {
                        barcode = "BC-001",
                        batchNumber = "B-001",
                        itemName = "Test Item",
                        quantity = 1m,
                        costPrice = 80m,
                        salesPrice = 100m,
                        mrp = 120m,
                        taxRatePercent = 18m,
                        isPriceIncludingTax = false,
                        inventoryBatchId = Guid.NewGuid(),
                        preTaxAmountBeforeDiscount = 100m,
                        itemDiscountAmount = 0m,
                        saleDiscountAmount = 0m,
                        taxableAmount = 100m,
                        taxAmount = 18m,
                        totalAmount = 118m,
                        itemDiscountOverrideType = (int)InstantDiscountType.None,
                        itemDiscountOverrideValue = 0m,
                    },
                },
            })
            .ToList();

        using var syncRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales/offline-sync");
        syncRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        syncRequest.Content = JsonContent.Create(new
        {
            deviceId = "device-1",
            sales,
        });

        var response = await client.SendAsync(syncRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ======================= EXISTING TESTS (PRESERVED) =======================

    [Fact]
    public async Task RecordSale_CreatesAllRelevantRecords()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);

        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 590m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 5m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);

        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();
        var invoiceNumber = saleBody.GetProperty("invoiceNumber").GetString()!;

        Assert.StartsWith("INV-", invoiceNumber);
        Assert.Equal(590m, saleBody.GetProperty("totalAmount").GetDecimal());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == saleId);
        Assert.NotNull(sale);
        Assert.Single(sale!.Items);
        Assert.Equal(590m, sale.TotalAmount);

        var tx = await db.StockTransactions
            .FirstOrDefaultAsync(t => t.InventoryBatchId == batchId && t.TransactionType == StockTransactionType.Out);
        Assert.NotNull(tx);
        Assert.Equal(-5m, tx!.Quantity);
        Assert.Equal(invoiceNumber, tx.ReferenceNumber);

        var batch = await db.InventoryBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        Assert.NotNull(batch);
        Assert.Equal(45m, batch!.Quantity);

        var inventory = await db.Inventory.FirstOrDefaultAsync(i => i.ItemId == itemId);
        Assert.NotNull(inventory);
        Assert.Equal(45m, inventory!.Quantity);
    }

    [Fact]
    public async Task RecordSale_WithCreditNoteRedemption_ConsumesBalanceAtomically()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var shopId = await GetShopIdFromTokenAsync(client, ownerToken);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Return Customer",
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
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Issue credit note",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);
        var recordBody = await recordResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleReturnId = recordBody.GetProperty("returns").EnumerateArray().Single().GetProperty("saleReturnId").GetGuid();

        var creditNoteResult = CreditNote.Issue(shopId, saleReturnId, 100m, "Issue credit note", $"CN-{Guid.NewGuid():N}", null);
        Assert.False(creditNoteResult.IsError);
        var creditNote = creditNoteResult.Value;
        await db.CreditNotes.AddAsync(creditNote);
        await db.SaveChangesAsync();
        Assert.Equal(100m, creditNote.AvailableBalance);

        using var creditSaleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        creditSaleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        creditSaleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Credit Note Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 68m,
            dueAmount = 0m,
            creditNoteAppliedAmount = 50m,
            creditNoteRedemptions = new[]
            {
                new
                {
                    code = creditNote.Code,
                    amount = 50m,
                },
            },
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
                },
            },
        });

        var creditSaleResponse = await client.SendAsync(creditSaleRequest);
        Assert.Equal(HttpStatusCode.Created, creditSaleResponse.StatusCode);
        var creditSaleBody = await creditSaleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var creditSaleId = creditSaleBody.GetProperty("saleId").GetGuid();

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedSale = await verificationDb.Sales.FirstAsync(s => s.Id == creditSaleId);
        var persistedCreditNote = await verificationDb.CreditNotes.Include(c => c.Redemptions).SingleAsync(c => c.Id == creditNote.Id);

        Assert.Equal(50m, persistedSale.CreditNoteAppliedAmount);
        Assert.Equal(50m, persistedCreditNote.AvailableBalance);
        Assert.Single(persistedCreditNote.Redemptions);
        Assert.Equal(50m, persistedCreditNote.Redemptions[0].Amount);
    }

    [Fact]
    public async Task RecordSale_WithMismatchedCreditNoteSplit_ReturnsValidationError()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = UniqueBarcode();
        var batchId = (await AddInventoryAsync(client, ownerToken, barcode, "B-001", 1m))
            .GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Return Customer",
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
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Issue credit note",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);
        var recordBody = await recordResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleReturnId = recordBody.GetProperty("returns").EnumerateArray().Single().GetProperty("saleReturnId").GetGuid();

        var shopId = await db.Shops.OrderByDescending(s => s.CreatedAt).Select(s => s.Id).FirstAsync();
        var creditNoteResult = CreditNote.Issue(shopId, saleReturnId, 100m, "Issue credit note", $"CN-{Guid.NewGuid():N}", null);
        Assert.False(creditNoteResult.IsError);
        var creditNote = creditNoteResult.Value;
        await db.CreditNotes.AddAsync(creditNote);
        await db.SaveChangesAsync();

        using var creditSaleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        creditSaleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        creditSaleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Credit Note Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 68m,
            dueAmount = 0m,
            creditNoteAppliedAmount = 50m,
            creditNoteRedemptions = new[]
            {
                new
                {
                    code = creditNote.Code,
                    amount = 10m,
                },
            },
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
                },
            },
        });

        var response = await client.SendAsync(creditSaleRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(Errors.Sale.CreditNoteRedemptionsSplitMismatch.Description, body);
    }

    [Fact]
    public async Task RecordSale_ServiceOnly_DoesNotMutateStock()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var serviceId = await AddServiceAsync(client, ownerToken, "Wheel Alignment", 500m);

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 590m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode = "SVC-001",
                    batchNumber = string.Empty,
                    itemName = "Wheel Alignment",
                    quantity = 1m,
                    costPrice = 0m,
                    salesPrice = 500m,
                    mrp = 0m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = Guid.Empty,
                    lineType = "Service",
                    serviceId,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId);

        Assert.Single(sale.Items);
        Assert.Equal(SaleLineType.Service, sale.Items[0].LineType);
        Assert.Equal(serviceId, sale.Items[0].ServiceId);
        Assert.Equal(0, await db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task RecordSale_Mixed_OnlyMutatesGoodsStock()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();
        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var serviceId = await AddServiceAsync(client, ownerToken, "Bike Wash", 500m);

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 708m,
            dueAmount = 0m,
            items = new object[]
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
                    lineType = "Goods",
                },
                new
                {
                    barcode = "SVC-002",
                    batchNumber = string.Empty,
                    itemName = "Bike Wash",
                    quantity = 1m,
                    costPrice = 0m,
                    salesPrice = 500m,
                    mrp = 0m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = Guid.Empty,
                    lineType = "Service",
                    serviceId,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saleId = (await saleResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("saleId").GetGuid();
        var sale = await db.Sales.Include(s => s.Items).SingleAsync(s => s.Id == saleId);
        var batch = await db.InventoryBatches.SingleAsync(x => x.Id == batchId);
        var inventory = await db.Inventory.SingleAsync(x => x.ItemId == itemId);
        var stockTransactions = await db.StockTransactions
            .Where(x => x.InventoryBatchId == batchId && x.TransactionType == StockTransactionType.Out)
            .ToListAsync();

        Assert.Equal(2, sale.Items.Count);
        Assert.Contains(sale.Items, x => x.LineType == SaleLineType.Goods);
        Assert.Contains(sale.Items, x => x.LineType == SaleLineType.Service && x.ServiceId == serviceId);
        Assert.Single(stockTransactions);
        Assert.Equal(9m, batch.Quantity);
        Assert.Equal(9m, inventory.Quantity);
    }

    [Fact]
    public async Task RecordSale_ReplayWithSameIdempotencyKey_ReturnsSameSaleWithoutDuplicateStockMutation()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);

        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();
        var idempotencyKey = $"sale-{Guid.NewGuid():N}";

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey,
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 590m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 5m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        replayRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        replayRequest.Content = JsonContent.Create(new
        {
            idempotencyKey,
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 590m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 5m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var replayResponse = await client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        var replayBody = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saleId, replayBody.GetProperty("saleId").GetGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var saleCount = await db.Sales.CountAsync(s => s.IdempotencyKey == idempotencyKey);
        Assert.Equal(1, saleCount);

        var outTxCount = await db.StockTransactions
            .CountAsync(t => t.InventoryBatchId == batchId && t.TransactionType == StockTransactionType.Out);
        Assert.Equal(1, outTxCount);

        var batch = await db.InventoryBatches.FirstOrDefaultAsync(b => b.Id == batchId);
        Assert.NotNull(batch);
        Assert.Equal(45m, batch!.Quantity);

        var inventory = await db.Inventory.FirstOrDefaultAsync(i => i.ItemId == itemId);
        Assert.NotNull(inventory);
        Assert.Equal(45m, inventory!.Quantity);
    }

    [Fact]
    public async Task RecordSale_ReplayPreservesWarnings()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();
        var idempotencyKey = $"sale-{Guid.NewGuid():N}";

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey,
            customerId = (Guid?)null,
            customerName = (string?)null,
            customerPhone = (string?)null,
            paymentMethod = (int)PaymentMethod.UPI,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 105m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(string.IsNullOrEmpty(saleBody.GetProperty("customerName").GetString()));
        Assert.True(string.IsNullOrEmpty(saleBody.GetProperty("customerPhone").GetString()));
        var warnings = saleBody.GetProperty("warnings")
            .EnumerateArray()
            .Select(w => w.GetString()!)
            .ToList();
        Assert.NotEmpty(warnings);

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        replayRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        replayRequest.Content = JsonContent.Create(new
        {
            idempotencyKey,
            customerId = (Guid?)null,
            customerName = (string?)null,
            customerPhone = (string?)null,
            paymentMethod = (int)PaymentMethod.UPI,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 105m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var replayResponse = await client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        var replayBody = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        var replayWarnings = replayBody.GetProperty("warnings")
            .EnumerateArray()
            .Select(w => w.GetString()!)
            .ToList();

        Assert.Equal(warnings, replayWarnings);
    }

    [Fact]
    public async Task RecordSale_WhenIdempotencyKeyConflicts_ReturnsConflict()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();
        var idempotencyKey = $"sale-{Guid.NewGuid():N}";

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey,
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
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        using var conflictRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        conflictRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        conflictRequest.Content = JsonContent.Create(new
        {
            idempotencyKey,
            customerId = (Guid?)null,
            customerName = "Walk-in Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var conflictResponse = await client.SendAsync(conflictRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saleCount = await db.Sales.CountAsync(s => s.IdempotencyKey == idempotencyKey);
        Assert.Equal(1, saleCount);
    }

    [Fact]
    public async Task RecordSale_WithPriceMismatch_ReturnsCreatedWithWarning()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = (string?)null,
            customerPhone = (string?)null,
            paymentMethod = (int)PaymentMethod.UPI,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 105m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);

        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var warnings = saleBody.GetProperty("warnings").EnumerateArray().ToList();
        Assert.NotEmpty(warnings);
    }

    // ======================= NEW: GET SALES LIST =======================

    [Fact]
    public async Task GetSales_ReturnsRecordedSales()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "List Customer",
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
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sales");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sales = listBody.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(sales);
        Assert.Contains(sales, s => s.GetProperty("customerName").GetString() == "List Customer");
        Assert.True(listBody.TryGetProperty("totalCount", out _));
        Assert.Equal(1, listBody.GetProperty("pageNumber").GetInt32());
        Assert.Equal(20, listBody.GetProperty("pageSize").GetInt32());
        Assert.True(listBody.TryGetProperty("summary", out _));
    }

    [Fact]
    public async Task GetSales_DefaultDateRange_ExcludesOlderSales()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var lease = await ReserveInvoiceLeaseAsync(client, ownerToken, deviceId: "device-filter");
        var prefix = lease.GetProperty("prefix").GetString()!;
        var padding = lease.GetProperty("numberPadding").GetInt32();
        var startNumber = lease.GetProperty("rangeStart").GetInt32();

        var oldInvoice = FormatInvoiceNumber(prefix, startNumber, padding);
        var recentInvoice = FormatInvoiceNumber(prefix, startNumber + 1, padding);

        await SyncOfflineSaleAsync(
            client,
            ownerToken,
            deviceId: "device-filter",
            invoiceNumber: oldInvoice,
            soldAt: DateTimeOffset.UtcNow.AddDays(-40),
            barcode,
            batchId);

        await SyncOfflineSaleAsync(
            client,
            ownerToken,
            deviceId: "device-filter",
            invoiceNumber: recentInvoice,
            soldAt: DateTimeOffset.UtcNow.AddDays(-1),
            barcode,
            batchId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.DoesNotContain(items, s => s.GetProperty("invoiceNumber").GetString() == oldInvoice);
        Assert.Contains(items, s => s.GetProperty("invoiceNumber").GetString() == recentInvoice);
        Assert.Equal(1, body.GetProperty("summary").GetProperty("invoiceCount").GetInt32());
    }

    [Fact]
    public async Task GetSales_Summary_IgnoresSearchAndStatus()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var lease = await ReserveInvoiceLeaseAsync(client, ownerToken, deviceId: "device-summary");
        var prefix = lease.GetProperty("prefix").GetString()!;
        var padding = lease.GetProperty("numberPadding").GetInt32();
        var startNumber = lease.GetProperty("rangeStart").GetInt32();

        var invoiceA = FormatInvoiceNumber(prefix, startNumber, padding);
        var invoiceB = FormatInvoiceNumber(prefix, startNumber + 1, padding);

        await SyncOfflineSaleAsync(
            client,
            ownerToken,
            deviceId: "device-summary",
            invoiceNumber: invoiceA,
            soldAt: DateTimeOffset.UtcNow.AddDays(-2),
            barcode,
            batchId);

        var saleBId = await SyncOfflineSaleAsync(
            client,
            ownerToken,
            deviceId: "device-summary",
            invoiceNumber: invoiceB,
            soldAt: DateTimeOffset.UtcNow.AddDays(-2),
            barcode,
            batchId);

        await RecordSimpleReturnAsync(client, ownerToken, saleBId, refundAmount: 118m);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?search=does-not-match&status=returned");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(2, body.GetProperty("summary").GetProperty("invoiceCount").GetInt32());
        Assert.Equal(118m, body.GetProperty("summary").GetProperty("refundAmount").GetDecimal());
        Assert.Equal(118m, body.GetProperty("summary").GetProperty("periodSales").GetDecimal());
    }

    // ======================= NEW: STATUS FIELDS & FILTERING =======================

    private static async Task<Guid> RecordSaleAsync(
        HttpClient client,
        string token,
        string barcode,
        Guid batchId,
        decimal paidAmount,
        decimal dueAmount,
        string customerName = "Status Test Customer",
        string customerPhone = "+919000000001")
    {
        Guid? customerId = null;
        if (dueAmount > 0)
        {
            customerId = await AddCustomerAsync(
                client, token,
                $"Due Customer {Guid.NewGuid():N}",
                $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
                true);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId,
            customerName,
            customerPhone,
            paymentMethod = (int)(dueAmount > 0 ? PaymentMethod.Credit : PaymentMethod.Cash),
            paidAmount,
            dueAmount,
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
                },
            },
        });
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("saleId").GetGuid();
    }

    [Fact]
    public async Task GetSales_ItemsIncludeStatusAndRefundFields()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var saleId = await RecordSaleAsync(client, ownerToken, barcode, batchId, paidAmount: 118m, dueAmount: 0m);
        await RecordSimpleReturnAsync(client, ownerToken, saleId, refundAmount: 118m);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sale = body.GetProperty("items").EnumerateArray()
            .First(s => s.GetProperty("saleId").GetGuid() == saleId);

        Assert.Equal("refunded", sale.GetProperty("status").GetString());
        Assert.Equal(118m, sale.GetProperty("refundAmount").GetDecimal());
        Assert.True(sale.TryGetProperty("dueReductionAmount", out _));
    }

    [Fact]
    public async Task GetSales_StatusFilter_Paid_ReturnsOnlyPaidSales()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var paidSaleId = await RecordSaleAsync(client, ownerToken, barcode, batchId, paidAmount: 118m, dueAmount: 0m);
        var partialSaleId = await RecordSaleAsync(client, ownerToken, barcode, batchId, paidAmount: 0m, dueAmount: 118m);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?status=paid");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, s => s.GetProperty("saleId").GetGuid() == paidSaleId);
        Assert.DoesNotContain(items, s => s.GetProperty("saleId").GetGuid() == partialSaleId);
        Assert.All(items, s => Assert.Equal("paid", s.GetProperty("status").GetString()));
        Assert.Equal(body.GetProperty("items").GetArrayLength(), body.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task GetSales_StatusFilter_PartiallyPaid_ReturnsOnlyPartialSales()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var paidSaleId = await RecordSaleAsync(client, ownerToken, barcode, batchId, paidAmount: 118m, dueAmount: 0m);
        var partialSaleId = await RecordSaleAsync(client, ownerToken, barcode, batchId, paidAmount: 0m, dueAmount: 118m);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?status=partiallyPaid");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, s => s.GetProperty("saleId").GetGuid() == partialSaleId);
        Assert.DoesNotContain(items, s => s.GetProperty("saleId").GetGuid() == paidSaleId);
        Assert.All(items, s => Assert.Equal("partiallyPaid", s.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task GetSales_StatusFilter_Refunded_ReturnsOnlyRefundedSales()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var paidSaleId = await RecordSaleAsync(client, ownerToken, barcode, batchId, paidAmount: 118m, dueAmount: 0m);
        var refundedSaleId = await RecordSaleAsync(client, ownerToken, barcode, batchId, paidAmount: 118m, dueAmount: 0m);
        await RecordSimpleReturnAsync(client, ownerToken, refundedSaleId, refundAmount: 118m);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?status=refunded");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, s => s.GetProperty("saleId").GetGuid() == refundedSaleId);
        Assert.DoesNotContain(items, s => s.GetProperty("saleId").GetGuid() == paidSaleId);
        Assert.All(items, s => Assert.Equal("refunded", s.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task GetSales_StatusFilter_Unknown_ReturnsOnlyUnknownSales()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var unknownSaleId = await RecordSaleAsync(
            client,
            ownerToken,
            barcode,
            batchId,
            paidAmount: 118m,
            dueAmount: 0m,
            customerName: "Unknown Status Customer",
            customerPhone: "909000000001");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rowsUpdated = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE sales
                SET due_amount = -20.00
                WHERE id = {unknownSaleId}
                """);
            Assert.Equal(1, rowsUpdated);
        }

        var paidSaleId = await RecordSaleAsync(
            client,
            ownerToken,
            barcode,
            batchId,
            paidAmount: 118m,
            dueAmount: 0m,
            customerName: "Paid Status Customer",
            customerPhone: "909000000002");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?status=unknown");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        Assert.Single(items);
        Assert.Contains(items, s => s.GetProperty("saleId").GetGuid() == unknownSaleId);
        Assert.DoesNotContain(items, s => s.GetProperty("saleId").GetGuid() == paidSaleId);
        Assert.All(items, s => Assert.Equal("unknown", s.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task GetSales_InvalidStatus_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?status=bogusStatus");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSales_Search_ByReturnNumber_ReturnsMatchingSale()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var saleId = await RecordSaleAsync(client, ownerToken, barcode, batchId, paidAmount: 118m, dueAmount: 0m);
        await RecordSimpleReturnAsync(client, ownerToken, saleId, refundAmount: 118m);

        // fetch the return number from GET /api/sales?status=refunded
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sales?status=refunded");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var returnNumbers = listBody.GetProperty("items").EnumerateArray()
            .First(s => s.GetProperty("saleId").GetGuid() == saleId)
            .GetProperty("returnNumbers").EnumerateArray()
            .Select(x => x.GetString()!)
            .ToList();
        Assert.NotEmpty(returnNumbers);
        var returnNumber = returnNumbers[0];

        using var searchRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/sales?search={returnNumber}");
        searchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var searchResponse = await client.SendAsync(searchRequest);

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchBody = await searchResponse.Content.ReadFromJsonAsync<JsonElement>();
        var searchItems = searchBody.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(searchItems, s => s.GetProperty("saleId").GetGuid() == saleId);
    }

    [Fact]
    public async Task GetSales_Search_ByInvoiceNumber_ReturnsMatchingSale()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var matchingSaleId = await RecordSaleAsync(
            client,
            ownerToken,
            barcode,
            batchId,
            paidAmount: 118m,
            dueAmount: 0m,
            customerName: "Invoice Search Customer",
            customerPhone: "918000000001");
        await RecordSaleAsync(
            client,
            ownerToken,
            barcode,
            batchId,
            paidAmount: 118m,
            dueAmount: 0m,
            customerName: "Invoice Other Customer",
            customerPhone: "918000000002");

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sales");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceNumber = listBody.GetProperty("items").EnumerateArray()
            .First(s => s.GetProperty("saleId").GetGuid() == matchingSaleId)
            .GetProperty("invoiceNumber")
            .GetString()!;
        Assert.NotEmpty(invoiceNumber);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sales?search={invoiceNumber}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, s => s.GetProperty("saleId").GetGuid() == matchingSaleId);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetSales_Search_ByCustomerName_ReturnsMatchingSale()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var matchingSaleId = await RecordSaleAsync(
            client,
            ownerToken,
            barcode,
            batchId,
            paidAmount: 118m,
            dueAmount: 0m,
            customerName: "CustomerSearchTarget",
            customerPhone: "917000000001");
        await RecordSaleAsync(
            client,
            ownerToken,
            barcode,
            batchId,
            paidAmount: 118m,
            dueAmount: 0m,
            customerName: "CustomerSearchOther",
            customerPhone: "917000000002");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?search=CustomerSearchTarget");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, s => s.GetProperty("saleId").GetGuid() == matchingSaleId);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetSales_Search_ByCustomerPhone_ReturnsMatchingSale()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var matchingSaleId = await RecordSaleAsync(
            client,
            ownerToken,
            barcode,
            batchId,
            paidAmount: 118m,
            dueAmount: 0m,
            customerName: "PhoneSearchTarget",
            customerPhone: "900000000001");
        await RecordSaleAsync(
            client,
            ownerToken,
            barcode,
            batchId,
            paidAmount: 118m,
            dueAmount: 0m,
            customerName: "PhoneSearchOther",
            customerPhone: "900000000002");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?search=900000000001");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, s => s.GetProperty("saleId").GetGuid() == matchingSaleId);
        Assert.Single(items);
    }

    [Fact]
    public async Task GetSales_Search_ByAmount_ReturnsMatchingSale()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var saleId = await RecordSaleAsync(client, ownerToken, barcode, batchId, paidAmount: 118m, dueAmount: 0m);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?search=118");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(items, s => s.GetProperty("saleId").GetGuid() == saleId);
    }

    [Fact]
    public async Task GetSales_Pagination_IsStableAndCapsPageSize()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        var lease = await ReserveInvoiceLeaseAsync(client, ownerToken, deviceId: "device-page");
        var prefix = lease.GetProperty("prefix").GetString()!;
        var padding = lease.GetProperty("numberPadding").GetInt32();
        var startNumber = lease.GetProperty("rangeStart").GetInt32();

        for (var i = 0; i < 5; i++)
        {
            var invoice = FormatInvoiceNumber(prefix, startNumber + i, padding);
            await SyncOfflineSaleAsync(
                client,
                ownerToken,
                deviceId: "device-page",
                invoiceNumber: invoice,
                soldAt: DateTimeOffset.UtcNow.AddMinutes(-i),
                barcode,
                batchId);
        }

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "/api/sales?page=1&pageSize=2");
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response1 = await client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadFromJsonAsync<JsonElement>();
        var page1 = body1.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("saleId").GetGuid()).ToList();

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "/api/sales?page=2&pageSize=2");
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response2 = await client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadFromJsonAsync<JsonElement>();
        var page2 = body2.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("saleId").GetGuid()).ToList();

        Assert.Equal(5, body1.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.DoesNotContain(page2, id => page1.Contains(id));

        using var capRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sales?pageSize=999");
        capRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var capResponse = await client.SendAsync(capRequest);
        Assert.Equal(HttpStatusCode.OK, capResponse.StatusCode);
        var capBody = await capResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(100, capBody.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetSales_NormalizesInvalidPaginationValues()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales?page=0&pageSize=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("pageNumber").GetInt32());
        Assert.Equal(20, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetSales_WithoutAuth_Returns401()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/api/sales");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSales_WithoutShop_Returns400()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ======================= NEW: GET SALE DETAIL =======================

    [Fact]
    public async Task GetSaleDetail_ReturnsSaleWithItems()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Detail Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{saleId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var detailResponse = await client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saleBody.GetProperty("invoiceNumber").GetString(), detail.GetProperty("invoiceNumber").GetString());
        Assert.Equal(236m, detail.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("Detail Customer", detail.GetProperty("customerName").GetString());
        Assert.Equal("+919876543210", detail.GetProperty("customerPhone").GetString());
        Assert.Single(detail.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetSaleDetail_IncludesRecordedLineHsnCode()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();
        var saleHsnCode = "0902";

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Detail HSN Customer",
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
                    hsnCode = saleHsnCode,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{saleId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var detailResponse = await client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleItem = detail.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(saleHsnCode, saleItem.GetProperty("hsnCode").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedSale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        Assert.Equal(saleHsnCode, persistedSale.Items.Single().HsnCode);
    }

    [Fact]
    public async Task GetSaleDetail_RecordedLineHsnCode_IsNotChangedByProductHsnUpdate()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-002", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();
        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var recordedHsnCode = "0902";
        var updatedHsnCode = "0910";

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Historic HSN Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 118m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-002",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                    hsnCode = recordedHsnCode,
                },
            },
        });

        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var updateItemRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/items/{itemId}");
        updateItemRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        updateItemRequest.Content = JsonContent.Create(new
        {
            name = "Test Item",
            barcode = "B-002",
            description = (string?)null,
            uom = "kg",
            hsnCode = updatedHsnCode,
            defaultTaxRatePercent = 0m,
        });

        var updateItemResponse = await client.SendAsync(updateItemRequest);
        Assert.Equal(HttpStatusCode.NoContent, updateItemResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedSaleItem = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        Assert.Equal(recordedHsnCode, persistedSaleItem.Items.Single().HsnCode);

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{saleId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var detailResponse = await client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleItem = detail.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(recordedHsnCode, saleItem.GetProperty("hsnCode").GetString());
    }

    [Fact]
    public async Task GetSaleDetail_ReturnsNullCustomerIdentityForWalkInSales()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = (string?)null,
            customerPhone = (string?)null,
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{saleId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var detailResponse = await client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(string.IsNullOrEmpty(detail.GetProperty("customerName").GetString()));
        Assert.True(string.IsNullOrEmpty(detail.GetProperty("customerPhone").GetString()));
    }

    [Fact]
    public async Task GetSaleDetail_NonExistentSale_ReturnsNotFound()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{Guid.NewGuid()}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSaleDetail_OtherShop_ReturnsNotFound()
    {
        using var client = CreateClient();
        var tokenA = await RegisterAsync(client);
        var ownerTokenA = await CreateShopAsync(client, tokenA);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerTokenA, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Cross Shop",
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
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        var tokenB = await RegisterAsync(client);
        var ownerTokenB = await CreateShopAsync(client, tokenB);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{saleId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ======================= NEW: RECORD SALE WITH DUE =======================

    [Fact]
    public async Task RecordSale_WithDue_CreatesCustomerLedgerEntry()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 50m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        Guid customerId;
        using (var addCustomerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/customers"))
        {
            addCustomerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            addCustomerRequest.Content = JsonContent.Create(new
            {
                name = "Due Customer",
                phoneNumber = "+919876543210",
                address = "12 Market Road",
                isActive = true,
            });

            var addCustomerResponse = await client.SendAsync(addCustomerRequest);
            Assert.Equal(HttpStatusCode.Created, addCustomerResponse.StatusCode);
            var addCustomerBody = await addCustomerResponse.Content.ReadFromJsonAsync<JsonElement>();
            customerId = addCustomerBody.GetProperty("customerId").GetGuid();
        }

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId,
            customerName = "Due Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Credit,
            paidAmount = 0m,
            dueAmount = 118m,
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
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(118m, saleBody.GetProperty("dueAmount").GetDecimal());
        Assert.Equal("Due Customer", saleBody.GetProperty("customerName").GetString());
        Assert.Equal("+919876543210", saleBody.GetProperty("customerPhone").GetString());

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{saleBody.GetProperty("saleId").GetGuid()}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var detailResponse = await client.SendAsync(detailRequest);

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Due Customer", detail.GetProperty("customerName").GetString());
        Assert.Equal("+919876543210", detail.GetProperty("customerPhone").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == saleBody.GetProperty("saleId").GetGuid());
        Assert.NotNull(sale);
        Assert.Equal(PaymentMethod.Credit, sale!.PaymentMethod);
    }

    [Fact]
    public async Task RecordSale_StaffCanRecordSale()
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
            lastName = "Sales",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var staffToken = await LoginAsync(client, staffEmail, staffPassword);
        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerScopedToken, barcode, "B-STAFF", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Staff Sale",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 118m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-STAFF",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);

        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
    }

    [Fact]
    public async Task RecordSale_WhenPaidDueDoNotMatchDiscountedTotal_ReturnsBadRequest()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-MISMATCH", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Mismatch",
            customerPhone = "+919876543200",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 999m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-MISMATCH",
                    itemName = "Test Item",
                    quantity = 1m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });

        var response = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ======================= NEW: SALE RETURN FLOW =======================

    [Fact]
    public async Task PreviewSaleReturn_ReturnsPreviewWithLineDetails()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Return Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleBody.GetProperty("saleId").GetGuid());
        var saleItemId = sale.Items[0].Id;

        using var previewRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{sale.Id}/returns/preview");
        previewRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        previewRequest.Content = JsonContent.Create(new
        {
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = (decimal?)null,
                    notes = (string?)null,
                },
            },
        });

        var previewResponse = await client.SendAsync(previewRequest);

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sale.Id, preview.GetProperty("saleId").GetGuid());
        Assert.True(preview.GetProperty("hasFinancialAccess").GetBoolean());
        Assert.Single(preview.GetProperty("lines").EnumerateArray());
        Assert.True(preview.GetProperty("warnings").GetArrayLength() >= 0);
    }

    [Fact]
    public async Task SaleReturn_UsesDiscountedPaidAmountForDefaultAndRequiresReasonForOverride()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-DISC-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var previewSaleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales/preview");
        previewSaleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        previewSaleRequest.Content = JsonContent.Create(new
        {
            saleDiscount = new { type = (int)InstantDiscountType.Flat, value = 5m },
            items = new[]
            {
                new
                {
                    inventoryBatchId = batchId,
                    barcode,
                    batchNumber = "B-DISC-001",
                    itemName = "Discount Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    itemDiscount = new { type = (int)InstantDiscountType.Flat, value = 10m },
                    clientLineKey = (string?)null,
                },
            },
        });

        var previewSaleResponse = await client.SendAsync(previewSaleRequest);
        Assert.Equal(HttpStatusCode.OK, previewSaleResponse.StatusCode);
        var previewSale = await previewSaleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var discountedTotal = previewSale.GetProperty("totalAmount").GetDecimal();

        using var recordSaleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        recordSaleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordSaleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Discount Return Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = discountedTotal,
            dueAmount = 0m,
            saleDiscount = new { type = (int)InstantDiscountType.Flat, value = 5m },
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-DISC-001",
                    itemName = "Discount Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                    itemDiscount = new { type = (int)InstantDiscountType.Flat, value = 10m },
                },
            },
        });

        var saleResponse = await client.SendAsync(recordSaleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItem = sale.Items.Single();

        var expectedMaxRefund = Math.Round(saleItem.TotalAmount / saleItem.Quantity, 2, MidpointRounding.AwayFromZero);
        var overrideRefundAmount = expectedMaxRefund + 1m;

        using var previewReturnRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns/preview");
        previewReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        previewReturnRequest.Content = JsonContent.Create(new
        {
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            items = new[]
            {
                new
                {
                    saleItemId = saleItem.Id,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = (decimal?)null,
                    notes = (string?)null,
                },
            },
        });

        var previewReturnResponse = await client.SendAsync(previewReturnRequest);
        Assert.Equal(HttpStatusCode.OK, previewReturnResponse.StatusCode);
        var previewReturn = await previewReturnResponse.Content.ReadFromJsonAsync<JsonElement>();
        var previewLine = previewReturn.GetProperty("lines").EnumerateArray().Single();
        var financial = previewLine.GetProperty("financial");
        Assert.Equal(expectedMaxRefund, financial.GetProperty("maxRefundAmount").GetDecimal());
        Assert.Equal(expectedMaxRefund, financial.GetProperty("approvedRefundAmount").GetDecimal());

        using var previewOverrideRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns/preview");
        previewOverrideRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        previewOverrideRequest.Content = JsonContent.Create(new
        {
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            items = new[]
            {
                new
                {
                    saleItemId = saleItem.Id,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = overrideRefundAmount,
                    notes = (string?)null,
                },
            },
        });

        var previewOverrideResponse = await client.SendAsync(previewOverrideRequest);
        Assert.Equal(HttpStatusCode.OK, previewOverrideResponse.StatusCode);
        var previewOverride = await previewOverrideResponse.Content.ReadFromJsonAsync<JsonElement>();
        var previewWarnings = previewOverride.GetProperty("warnings")
            .EnumerateArray()
            .Select(w => w.GetProperty("code").GetString()!)
            .ToList();
        Assert.Contains("sale_return.note_required.refund_override", previewWarnings);

        using var recordOverrideNoReasonRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordOverrideNoReasonRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordOverrideNoReasonRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Override refund",
            items = new[]
            {
                new
                {
                    saleItemId = saleItem.Id,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = overrideRefundAmount,
                    notes = (string?)null,
                },
            },
        });

        var recordNoReasonResponse = await client.SendAsync(recordOverrideNoReasonRequest);
        Assert.Equal(HttpStatusCode.BadRequest, recordNoReasonResponse.StatusCode);
        var problem = await recordNoReasonResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SaleReturn.RefundOverrideReasonRequired", problem.GetProperty("title").GetString());

        using var recordOverrideWithReasonRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordOverrideWithReasonRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordOverrideWithReasonRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Override refund",
            items = new[]
            {
                new
                {
                    saleItemId = saleItem.Id,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = overrideRefundAmount,
                    notes = "Goodwill",
                },
            },
        });

        var recordWithReasonResponse = await client.SendAsync(recordOverrideWithReasonRequest);
        Assert.Equal(HttpStatusCode.OK, recordWithReasonResponse.StatusCode);
        var recordedSale = await recordWithReasonResponse.Content.ReadFromJsonAsync<JsonElement>();
        var returnEntry = recordedSale.GetProperty("returns").EnumerateArray().Single();
        var returnItem = returnEntry.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(overrideRefundAmount, returnItem.GetProperty("approvedRefundAmount").GetDecimal());

        var persistedReturn = await db.SaleReturns.Include(r => r.Items).SingleAsync(r => r.SaleId == saleId);
        var persistedItem = persistedReturn.Items.Single();
        Assert.Equal(expectedMaxRefund, persistedItem.MaxRefundAmount);
        Assert.Equal(overrideRefundAmount, persistedItem.ApprovedRefundAmount);
        Assert.Equal("Goodwill", persistedItem.Notes);
    }

    [Fact]
    public async Task RecordSaleReturn_Restockable_UpdatesStockAndCreatesReturn()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Return Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Return one unit restockable",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);

        var recordBody = await recordResponse.Content.ReadFromJsonAsync<JsonElement>();

        var returns = recordBody.GetProperty("returns").EnumerateArray().ToList();
        var returnEntry = Assert.Single(returns);
        Assert.Equal(118m, returnEntry.GetProperty("totalRefundAmount").GetDecimal());

        var batch = await db.InventoryBatches.FirstAsync(b => b.Id == batchId);
        Assert.Equal(9m, batch.Quantity);

        var inv = await db.Inventory.FirstAsync(i => i.ItemId == itemId);
        Assert.Equal(9m, inv.Quantity);
    }

    [Fact]
    public async Task RecordSaleReturn_Wastage_DoesNotRestock()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var itemId = inboundBody.GetProperty("itemId").GetGuid();
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Wastage Customer",
            customerPhone = "+919876543210",
            paymentMethod = (int)PaymentMethod.Cash,
            paidAmount = 236m,
            dueAmount = 0m,
            items = new[]
            {
                new
                {
                    barcode,
                    batchNumber = "B-001",
                    itemName = "Test Item",
                    quantity = 2m,
                    costPrice = 80m,
                    salesPrice = 100m,
                    mrp = 120m,
                    taxRatePercent = 18m,
                    isPriceIncludingTax = false,
                    inventoryBatchId = batchId,
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Return wastage",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Wastage,
                    approvedRefundAmount = 100m,
                    notes = "Damaged packaging",
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);

        var batch = await db.InventoryBatches.FirstAsync(b => b.Id == batchId);
        Assert.Equal(8m, batch.Quantity);

        var inv = await db.Inventory.FirstAsync(i => i.ItemId == itemId);
        Assert.Equal(8m, inv.Quantity);
    }

    [Fact]
    public async Task VoidSaleReturn_ReturnsNoContent()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Void Return Customer",
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
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Return to void",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);

        var recordBody = await recordResponse.Content.ReadFromJsonAsync<JsonElement>();
        var returns = recordBody.GetProperty("returns").EnumerateArray().ToList();
        var saleReturnId = returns[0].GetProperty("saleReturnId").GetGuid();

        using var voidRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/returns/{saleReturnId}/void");
        voidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        voidRequest.Content = JsonContent.Create(new
        {
            reason = "Customer changed mind",
        });

        var voidResponse = await client.SendAsync(voidRequest);
        Assert.Equal(HttpStatusCode.NoContent, voidResponse.StatusCode);
    }

    [Fact]
    public async Task GetSaleByReturnNumber_ReturnsSale()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId = (Guid?)null,
            customerName = "Lookup Customer",
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
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Lookup return",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                },
            },
        });

        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);
        var recordBody = await recordResponse.Content.ReadFromJsonAsync<JsonElement>();
        var returnNumber = recordBody.GetProperty("returns")[0].GetProperty("returnNumber").GetString()!;

        using var lookupRequest = new HttpRequestMessage(
            HttpMethod.Get, $"/api/sales/returns/{returnNumber}");
        lookupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var lookupResponse = await client.SendAsync(lookupRequest);

        Assert.Equal(HttpStatusCode.OK, lookupResponse.StatusCode);
        var lookup = await lookupResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(saleId, lookup.GetProperty("saleId").GetGuid());
    }

    [Fact]
    public async Task GetSaleByReturnNumber_NonExistent_ReturnsNoContent()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/sales/returns/NONEXISTENT");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RecordSaleReturn_StaffGetsForbidden()
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
            lastName = "Returns",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/{Guid.NewGuid()}/returns");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        request.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = (string?)null,
            items = Array.Empty<object>(),
        });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VoidSaleReturn_StaffGetsForbidden()
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
            lastName = "Void",
            phoneNumber = "+919876543211",
            password = staffPassword,
            confirmPassword = staffPassword,
            role = "Staff",
        });
        var addUserResponse = await client.SendAsync(addUserRequest);
        addUserResponse.EnsureSuccessStatusCode();

        var staffToken = await LoginAsync(client, staffEmail, staffPassword);

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/sales/returns/{Guid.NewGuid()}/void");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        request.Content = JsonContent.Create(new { reason = "Test" });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RecordSale_ConcurrentCreditNoteRedemptions_OnlyOneSucceeds()
    {
        using var client = CreateClient();
        var token = await RegisterAsync(client);
        var ownerToken = await CreateShopAsync(client, token);
        var shopId = await GetShopIdFromTokenAsync(client, ownerToken);

        var barcode = UniqueBarcode();
        var inboundBody = await AddInventoryAsync(client, ownerToken, barcode, "B-001", 10m);
        var batchId = inboundBody.GetProperty("inventoryBatchId").GetGuid();
        var serviceId = await AddServiceAsync(client, ownerToken, "Concurrent Credit Note Service", 100m);

        using var saleRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
        saleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        saleRequest.Content = JsonContent.Create(new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerName = "Return Customer",
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
                },
            },
        });
        var saleResponse = await client.SendAsync(saleRequest);
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        var saleBody = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = saleBody.GetProperty("saleId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == saleId);
        var saleItemId = sale.Items[0].Id;

        using var recordReturnRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/returns");
        recordReturnRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        recordReturnRequest.Content = JsonContent.Create(new
        {
            payoutMethod = (int)PaymentMethod.Cash,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Issue credit note for concurrency test",
            items = new[]
            {
                new
                {
                    saleItemId,
                    quantity = 1m,
                    condition = (int)SaleReturnCondition.Restockable,
                    approvedRefundAmount = 118m,
                    notes = (string?)null,
                },
            },
        });
        var recordResponse = await client.SendAsync(recordReturnRequest);
        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);
        var recordBody = await recordResponse.Content.ReadFromJsonAsync<JsonElement>();
        var saleReturnId = recordBody.GetProperty("returns").EnumerateArray().Single().GetProperty("saleReturnId").GetGuid();

        var creditNoteResult = CreditNote.Issue(shopId, saleReturnId, 100m, "Concurrency test note", $"CN-{Guid.NewGuid():N}", null);
        Assert.False(creditNoteResult.IsError);
        var creditNote = creditNoteResult.Value;
        await db.CreditNotes.AddAsync(creditNote);
        await db.SaveChangesAsync();

        // Use distinct inventory rows so credit note remains the only contested write target.
        using var client2 = CreateClient();

        var task1 = Task.Run(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            req.Content = JsonContent.Create(new
            {
                idempotencyKey = $"sale-conc-1-{Guid.NewGuid():N}",
                customerName = "Concurrent Customer",
                customerPhone = "+919876543210",
                paymentMethod = (int)PaymentMethod.Cash,
                paidAmount = 68m,
                dueAmount = 0m,
                creditNoteAppliedAmount = 50m,
                creditNoteRedemptions = new[] { new { code = creditNote.Code, amount = 50m } },
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
                    },
                },
            });
            return await client.SendAsync(req);
        });

        var task2 = Task.Run(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/sales");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            req.Content = JsonContent.Create(new
            {
                idempotencyKey = $"sale-conc-2-{Guid.NewGuid():N}",
                customerName = "Concurrent Customer",
                customerPhone = "+919876543210",
                paymentMethod = (int)PaymentMethod.Cash,
                paidAmount = 68m,
                dueAmount = 0m,
                creditNoteAppliedAmount = 50m,
                creditNoteRedemptions = new[] { new { code = creditNote.Code, amount = 50m } },
                items = new[]
                {
                    new
                    {
                        barcode = "SVC-CONC-001",
                        batchNumber = string.Empty,
                        itemName = "Concurrent Credit Note Service",
                        quantity = 1m,
                        costPrice = 0m,
                        salesPrice = 100m,
                        mrp = 0m,
                        taxRatePercent = 18m,
                        isPriceIncludingTax = false,
                        inventoryBatchId = Guid.Empty,
                        lineType = "Service",
                        serviceId,
                    },
                },
            });
            return await client2.SendAsync(req);
        });

        var responses = await Task.WhenAll(task1, task2);
        var statuses = responses.Select(r => r.StatusCode).ToList();

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persistedNote = await verificationDb.CreditNotes
            .Include(c => c.Redemptions)
            .SingleAsync(c => c.Id == creditNote.Id);

        Assert.Equal(50m, persistedNote.AvailableBalance);
        Assert.Single(persistedNote.Redemptions);
    }
}
