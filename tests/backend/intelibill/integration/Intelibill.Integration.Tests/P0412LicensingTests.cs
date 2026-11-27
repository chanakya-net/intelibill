using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0412LicensingTests
{
    [Theory]
    [InlineData("Customer")]
    [InlineData("Supplier")]
    public async Task Concurrent_payments_share_one_proven_due(string partyType)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var party = await CreatePartyAsync(client, partyType);
        var source = await CreateDueAsync(client, shop, partyType, party);
        _clock.Now = end;

        var route = PaymentRoute(partyType, party);
        var first = Payment(70m);
        var second = Payment(70m);
        var responses = await Task.WhenAll(client.PostAsJsonAsync(route, first, Ct), client.PostAsJsonAsync(route, second, Ct));
        using var a = responses[0];
        using var b = responses[1];
        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1,
            $"{partyType}: expected one ₹70 payment and one conflict, received {(int)a.StatusCode}/{(int)b.StatusCode}.");
        var winner = a.IsSuccessStatusCode ? first : second;
        var outcome = await ReadSuccessAsync(a.IsSuccessStatusCode ? a : b);
        await AssertProblemAsync(a.IsSuccessStatusCode ? b : a, HttpStatusCode.Conflict, "Payment.SettlementLimitExceeded");
        Assert.Equal(30m, await ReadLimitAsync(client, partyType, party));
        using var settled = await client.PostAsJsonAsync(route, Payment(30m, [new { targetId = source, amount = 20m }]), Ct);
        await ReadSuccessAsync(settled);
        await AssertProblemAsync(await client.PostAsJsonAsync(route, Payment(1m), Ct), HttpStatusCode.Conflict, "Payment.SettlementLimitExceeded");
        Assert.Equal(0m, await ReadLimitAsync(client, partyType, party));

        using var replay = await client.PostAsJsonAsync(route, winner, Ct);
        using var lookup = await client.GetAsync($"/api/financial-operations/{partyType.ToLowerInvariant()}-payment/{winner.OperationKey}", Ct);
        Assert.True(JsonElement.DeepEquals(outcome, await ReadSuccessAsync(replay)));
        var recorded = await ReadSuccessAsync(lookup);
        Assert.Equal(winner.OperationKey, recorded.GetProperty("operationKey").GetString());
        Assert.Equal(party, recorded.GetProperty("targetId").GetGuid());
        await using var db = Open(Migrator);
        var stored = await db.OperationReceipts.SingleAsync(x => x.ShopId == shop && x.OperationKey == winner.OperationKey, Ct);
        using var persisted = JsonDocument.Parse(stored.ResponseBody!);
        Assert.True(JsonElement.DeepEquals(persisted.RootElement, recorded.GetProperty("response")));
        Assert.Equal(outcome.GetProperty("receiptId").GetGuid(), persisted.RootElement.GetProperty("receiptId").GetGuid());
        Assert.Equal(2, await db.PaymentReceipts.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(2, await db.OperationReceipts.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(partyType == "Supplier" ? 2 : 0, await db.Expenses.CountAsync(x => x.ShopId == shop, Ct));
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Supplier")]
    public async Task Zero_due_refuses_an_advance_at_the_party_lock(string partyType)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var party = await CreatePartyAsync(client, partyType);
        await using var holder = Open(Migrator);
        await holder.Database.OpenConnectionAsync(Ct);
        await using var transaction = await holder.Database.BeginTransactionAsync(Ct);
        if (partyType == "Customer")
            await holder.Database.ExecuteSqlAsync($"SELECT id FROM customers WHERE id = {party} FOR UPDATE", Ct);
        else
            await holder.Database.ExecuteSqlAsync($"SELECT id FROM suppliers WHERE id = {party} FOR UPDATE", Ct);
        var blocker = ((Npgsql.NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
        var pending = client.PostAsJsonAsync(PaymentRoute(partyType, party), Payment(10m), Ct);
        await WaitForBusinessLockAsync(pending, blocker);
        _clock.Now = end;
        await transaction.RollbackAsync(Ct);
        await AssertProblemAsync(await pending, HttpStatusCode.Conflict, "Payment.SettlementLimitExceeded");

        await using var db = Open(Migrator);
        Assert.False(await db.PaymentReceipts.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.OperationReceipts.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.Expenses.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.ExpenseCategories.AnyAsync(x => x.ShopId == shop && x.Name == "Supplier Payments", Ct));
    }

    [Fact]
    public async Task Existing_receipt_operations_replay_while_restricted()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        using var manager = CreateClient(host);
        using var staff = CreateClient(host);
        await AddMemberAsync(host, client, manager, shop, "Manager");
        await AddMemberAsync(host, client, staff, shop, "Staff");
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var party = await CreatePartyAsync(client, "Customer");
        var source = await CreateDueAsync(client, shop, "Customer", party);
        var route = PaymentRoute("Customer", party);
        using var paid = await client.PostAsJsonAsync(route, Payment(100m), Ct);
        var receipt = (await ReadSuccessAsync(paid)).GetProperty("receiptId").GetGuid();
        _clock.Now = end;
        var allocation = new { operationKey = Guid.NewGuid().ToString(), allocations = new[] { new { targetId = source, amount = 100m } } };
        var allocate = $"/api/payments/{receipt}/allocations";
        var reverse = $"/api/payments/{receipt}/reversal";
        using var staffDenied = await staff.PostAsJsonAsync(allocate, allocation, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, staffDenied.StatusCode);
        using var allocated = await manager.PostAsJsonAsync(allocate, allocation, Ct);
        var allocatedBody = await ReadSuccessAsync(allocated);
        var reversal = new { operationKey = Guid.NewGuid().ToString(), reason = "Duplicate collection" };
        using var managerDenied = await manager.PostAsJsonAsync(reverse, reversal, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, managerDenied.StatusCode);
        using var reversed = await client.PostAsJsonAsync(reverse, reversal, Ct);
        var reversedBody = await ReadSuccessAsync(reversed);
        using var retryAllocation = await manager.PostAsJsonAsync(allocate, allocation, Ct);
        using var retryReversal = await client.PostAsJsonAsync(reverse, reversal, Ct);
        Assert.True(JsonElement.DeepEquals(allocatedBody, await ReadSuccessAsync(retryAllocation)));
        Assert.True(JsonElement.DeepEquals(reversedBody, await ReadSuccessAsync(retryReversal)));

        await AssertProblemAsync(await manager.PostAsJsonAsync(allocate,
            allocation with { allocations = [new { targetId = source, amount = 99m }] }, Ct),
            HttpStatusCode.Conflict, "Operation.PayloadMismatch");
        await ApplyAsync(binding, end.AddHours(1), revision: 2, restriction: "Security");
        await AssertProblemAsync(await client.PostAsJsonAsync(reverse, reversal, Ct), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertProblemAsync(await manager.PostAsJsonAsync(allocate, allocation, Ct), HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
    }

    private static string PaymentRoute(string partyType, Guid party) =>
        $"/api/{(partyType == "Customer" ? "customers" : "suppliers")}/{party}/payments";

    private PaymentInput Payment(decimal amount, object[]? allocations = null) =>
        new(amount, BusinessCalendar.DateOf(_clock.Now),
            Guid.NewGuid().ToString(), allocations ?? []);

    private sealed record PaymentInput(decimal Amount, DateOnly PaymentDate, string? OperationKey, object[] Allocations);

    private static async Task<decimal> ReadLimitAsync(HttpClient client, string partyType, Guid party)
    {
        using var response = await client.GetAsync($"/api/payments/balances?partyType={partyType}&partyId={party}", Ct);
        return (await ReadSuccessAsync(response)).GetProperty("remainingSettlementDue").GetDecimal();
    }

    private static async Task<Guid> CreatePartyAsync(HttpClient client, string partyType)
    {
        if (partyType == "Customer")
        {
            using var customer = await client.PostAsJsonAsync("/api/customers", new { name = "Customer", phoneNumber = $"+9198{Random.Shared.Next(10000000, 99999999)}", isActive = true, creditLimit = 1_000_000m, creditPolicyMode = "Enforced" }, Ct);
            return (await ReadSuccessAsync(customer)).GetProperty("customerId").GetGuid();
        }

        using var supplier = await client.PostAsJsonAsync("/api/suppliers", new
            { name = $"Supplier {Guid.NewGuid():N}"[..20], address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pin = "560001", isActive = true }, Ct);
        var body = await ReadSuccessAsync(supplier);
        return (body.TryGetProperty("supplierId", out var id) ? id : body.GetProperty("id")).GetGuid();
    }

    private async Task<Guid> CreateDueAsync(HttpClient client, Guid shop, string partyType, Guid party)
    {
        if (partyType == "Customer")
            return await SellOnCreditAsync(client, party);

        using var inbound = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = $"Receipt item {Guid.NewGuid():N}", barcode = $"PAY-{Guid.NewGuid():N}", uom = "pcs", batchNumber = "P0412",
            quantity = 10m, totalPurchaseCost = 100m, mrp = 120m, salesPrice = 100m, taxRatePercent = 0m, supplierId = party,
        }, Ct);
        var batch = (await ReadSuccessAsync(inbound)).GetProperty("inventoryBatchId").GetGuid();
        await using var db = Open(Migrator);
        return await db.PurchaseReceiptValues.Where(r => r.ShopId == shop && r.InventoryBatchId == batch)
            .Select(r => r.Id).SingleAsync(Ct);
    }

    private static async Task<Guid> SellOnCreditAsync(HttpClient client, Guid customer)
    {
        var serviceName = $"Repair {Guid.NewGuid():N}";
        using var service = await client.PostAsJsonAsync("/api/services", new
            { name = serviceName, price = 100m, taxRatePercent = 0m, taxIncluded = false, isActive = true }, Ct);
        var serviceId = (await ReadSuccessAsync(service)).GetProperty("serviceId").GetGuid();
        using var sale = await client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}", customerId = customer, customerName = "Customer",
            customerPhone = "+919812345678", paymentMethod = (int)PaymentMethod.Credit, paidAmount = 0m, dueAmount = 100m,
            items = new[] { new { barcode = "SVC-PAY", batchNumber = "", itemName = serviceName, quantity = 1m,
                costPrice = 0m, salesPrice = 100m, mrp = 0m, taxRatePercent = 0m, isPriceIncludingTax = false,
                inventoryBatchId = Guid.Empty, lineType = "Service", serviceId } },
        }, Ct);
        return (await ReadSuccessAsync(sale)).GetProperty("saleId").GetGuid();
    }

    private async Task WaitForBusinessLockAsync(Task request, int blocker)
    {
        await using var observer = Open(Migrator);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            var waiter = await observer.Database.SqlQuery<int>($"""
                SELECT pid AS "Value" FROM pg_stat_activity
                WHERE wait_event_type = 'Lock' AND {blocker} = ANY(pg_blocking_pids(pid)) LIMIT 1
                """).SingleOrDefaultAsync(deadline.Token);
            if (waiter != 0) return;
            Assert.False(request.IsCompleted, "Request completed before reaching the party lock.");
            await Task.Delay(10, deadline.Token);
        }
    }
}
