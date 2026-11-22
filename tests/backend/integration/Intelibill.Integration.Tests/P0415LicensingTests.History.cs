using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Services.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelibill.Integration.Tests;

public sealed partial class P0415LicensingTests
{
    [Fact]
    public async Task Expired_history_and_committed_recovery_keep_original_records()
    {
        using var key = RSA.Create(2048);
        // Compare statements for the same business date even when licence expiry crosses shop midnight.
        var statementClock = new BusinessClock(new TestClock(_clock.Now));
        using var host = CreateHost(key, services =>
        {
            services.RemoveAll<IBusinessClock>();
            services.AddSingleton<IBusinessClock>(statementClock);
        });
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var delivery = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var grant = await BindAsync(shop);
        await DeliverAsync(delivery, key, grant);
        await EnforceAsync(shop);

        var history = await SeedHistoricalAccountsAsync(client, shop);
        var original = await ReadHistoryAsync(client, history);
        _clock.Now = grant.Shop!.AccessEndsAt!.Value;
        var expired = await ReadHistoryAsync(client, history);
        foreach (var path in history)
            Assert.True(JsonElement.DeepEquals(original[path], expired[path]), path);

        await DeliverAsync(delivery, key, grant with
        {
            EventId = Guid.NewGuid(),
            Account = grant.Account! with { Revision = 2, Restriction = "Security" },
        });
        foreach (var path in history)
            await AssertSecurityDenialAsync(client, path);
    }

    [Fact]
    public async Task Account_security_keeps_source_authorized_member_removal_available()
    {
        using var key = RSA.Create(2048);
        using var host = CreateHost(key);
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var employee = CreateClient(host);
        using var delivery = CreateClient(host);
        var shop = await CreateShopAsync(owner);
        await CreateShopAsync(employee);
        var token = new JwtSecurityToken(employee.DefaultRequestHeaders.Authorization!.Parameter);
        var user = Guid.Parse(token.Subject);
        var grant = await BindAsync(shop);
        await DeliverAsync(delivery, key, grant);
        await EnforceAsync(shop);
        var membership = ShopMembership.Create(shop, user, ShopRole.Staff, isDefault: false);
        await using (var db = OpenMigrator())
        {
            db.Add(membership);
            await db.SaveChangesAsync(Ct);
        }

        using var switched = await employee.PostAsJsonAsync("/api/shops/switch", new { shopId = shop }, Ct);
        employee.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(switched)).GetProperty("accessToken").GetString());
        using var staffExport = await employee.GetAsync("/api/exports/sales", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, staffExport.StatusCode);

        await DeliverAsync(delivery, key, grant with
        {
            EventId = Guid.NewGuid(),
            Account = grant.Account! with { Revision = 2, Restriction = "Security" },
        });
        using var removed = await owner.PatchAsJsonAsync($"/api/shops/{shop:D}/members/{user:D}",
            new { role = "Staff", status = "Removed", expectedVersion = membership.AuthorityVersion }, Ct);
        var receipt = await ReadSuccessAsync(removed);
        Assert.Equal("Removed", receipt.GetProperty("status").GetString());
        Assert.False(receipt.TryGetProperty("isSourceAuthorizedRemoval", out _));
    }

    private async Task<string[]> SeedHistoricalAccountsAsync(HttpClient client, Guid shop)
    {
        using var newCustomer = await client.PostAsJsonAsync("/api/customers", new
        {
            name = "History fixture customer", phoneNumber = "+919812345678", isActive = true,
        }, Ct);
        var customer = (await ReadSuccessAsync(newCustomer)).GetProperty("customerId").GetGuid();
        var saleKey = $"p0415-sale-{Guid.NewGuid():N}";
        await CreateSaleAsync(client, customer, saleKey);
        var paymentKey = $"p0415-payment-{Guid.NewGuid():N}";
        using var newPayment = await client.PostAsJsonAsync($"/api/customers/{customer:D}/payments", new
        {
            amount = 1m, paymentDate = DateOnly.FromDateTime(_clock.Now.DateTime.AddHours(5.5)),
            operationKey = paymentKey, method = "Cash",
        }, Ct);
        await ReadSuccessAsync(newPayment);

        using var newSupplier = await client.PostAsJsonAsync("/api/suppliers", new
        {
            name = "History fixture supplier", address = "42 MG Road", city = "Bengaluru", state = "Karnataka",
            pin = "560001", isActive = true,
        }, Ct);
        var supplierBody = await ReadSuccessAsync(newSupplier);
        var supplier = (supplierBody.TryGetProperty("supplierId", out var id) ? id : supplierBody.GetProperty("id")).GetGuid();
        using var inbound = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "History receipt item", barcode = Guid.NewGuid().ToString("N"), uom = "pcs", batchNumber = "HISTORY",
            quantity = 5m, totalPurchaseCost = 10m, mrp = 10m, salesPrice = 8m, taxRatePercent = 0m, supplierId = supplier,
        }, Ct);
        var batch = (await ReadSuccessAsync(inbound)).GetProperty("inventoryBatchId").GetGuid();
        await using var db = OpenMigrator();
        var receipt = await db.PurchaseReceiptValues.Where(row => row.ShopId == shop && row.InventoryBatchId == batch)
            .Select(row => row.Id).SingleAsync(Ct);
        return
        [
            $"/api/sales/by-idempotency-key/{saleKey}",
            $"/api/customers/{customer:D}/account",
            $"/api/financial-operations/customer-payment/{paymentKey}",
            $"/api/suppliers/{supplier:D}/ledger",
            $"/api/purchase-receipts/{receipt:D}",
        ];
    }

    private static async Task<Dictionary<string, JsonElement>> ReadHistoryAsync(HttpClient client, string[] paths)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            using var response = await client.GetAsync(path, Ct);
            result.Add(path, await ReadSuccessAsync(response));
        }
        return result;
    }
}
