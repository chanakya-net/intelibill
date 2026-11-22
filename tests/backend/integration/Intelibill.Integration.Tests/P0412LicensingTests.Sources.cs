using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0412LicensingTests
{
    [Fact]
    public async Task Restricted_payment_refuses_backdated_and_foreign_sources_without_writes()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var foreign = CreateClient(host);
        var shop = await CreateShopAsync(client);
        await CreateShopAsync(foreign);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var customer = await CreatePartyAsync(client, "Customer");
        var other = await CreatePartyAsync(client, "Customer");
        var foreignCustomer = await CreatePartyAsync(foreign, "Customer");
        var source = await SellOnCreditAsync(client, customer);
        var otherSource = await SellOnCreditAsync(client, other);
        var foreignSource = await SellOnCreditAsync(foreign, foreignCustomer);
        _clock.Now = end;

        var route = PaymentRoute("Customer", customer);
        var today = Payment(10m, [new { targetId = source, amount = 10m }]);
        var backdated = today with { PaymentDate = today.PaymentDate.AddDays(-2) };
        await AssertProblemAsync(await client.PostAsJsonAsync(route, backdated, Ct),
            HttpStatusCode.Conflict, "Payment.SettlementSourceIneligible");
        foreach (var id in new[] { otherSource, foreignSource, Guid.NewGuid() })
        {
            await AssertProblemAsync(await client.PostAsJsonAsync(route, Payment(10m, [new { targetId = id, amount = 10m }]), Ct),
                HttpStatusCode.NotFound, "Payment.AllocationTargetNotFound");
        }

        await using var db = Open(Migrator);
        Assert.Equal(2, await db.CustomerLedgerEntries.CountAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.PaymentReceipts.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.PaymentAllocations.AnyAsync(x => x.ShopId == shop, Ct));
        Assert.False(await db.OperationReceipts.AnyAsync(x => x.ShopId == shop, Ct));
        using var paid = await client.PostAsJsonAsync(route, today, Ct);
        Assert.Equal(90m, (await ReadSuccessAsync(paid)).GetProperty("runningBalance").GetDecimal());
    }

    [Fact]
    public async Task Supplier_cap_nets_unallocated_money_and_surplus_return_credit_even_with_unsupported_ledger_debt()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var supplier = await CreatePartyAsync(client, "Supplier");
        var fullyPaid = await CreateDueAsync(client, shop, "Supplier", supplier);
        await CreateDueAsync(client, shop, "Supplier", supplier);
        var route = PaymentRoute("Supplier", supplier);
        await ReadSuccessAsync(await client.PostAsJsonAsync(route, Payment(100m, [new { targetId = fullyPaid, amount = 100m }]), Ct));
        await ReadSuccessAsync(await client.PostAsJsonAsync(route, Payment(20m), Ct));
        await AcceptReturnCreditAsync(client, supplier, fullyPaid);
        await InsertUnsupportedBalanceAsync(shop, supplier, 500m);

        _clock.Now = end;
        Assert.Equal(50m, await ReadLimitAsync(client, "Supplier", supplier));
        await AssertProblemAsync(await client.PostAsJsonAsync(route, Payment(50.01m), Ct),
            HttpStatusCode.Conflict, "Payment.SettlementLimitExceeded");
        using var settled = await client.PostAsJsonAsync(route, Payment(50m), Ct);
        var receipt = (await ReadSuccessAsync(settled)).GetProperty("receiptId").GetGuid();
        Assert.Equal(0m, await ReadLimitAsync(client, "Supplier", supplier));

        using var reversed = await client.PostAsJsonAsync($"/api/payments/{receipt}/reversal",
            new { operationKey = Guid.NewGuid().ToString(), reason = "Payment duplicated" }, Ct);
        var expenseId = (await ReadSuccessAsync(reversed)).GetProperty("expenseId").GetGuid();
        Assert.Equal(50m, await ReadLimitAsync(client, "Supplier", supplier));
        await using var db = Open(Migrator);
        Assert.True((await db.Expenses.SingleAsync(e => e.Id == expenseId, Ct)).IsVoided);
        Assert.Equal(3, await db.PaymentReceipts.CountAsync(x => x.ShopId == shop, Ct));
    }

    [Theory]
    [InlineData(true, "Active", "Unrestricted", false, false)]
    [InlineData(false, "Active", "Unrestricted", true, false)]
    [InlineData(true, "PendingActivation", "Unrestricted", false, true)]
    [InlineData(true, "Active", "Trading", false, true)]
    public async Task Advance_treatment_depends_on_local_enrollment_and_current_authority(
        bool enrolled, string state, string restriction, bool expired, bool limited)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var customer = await CreatePartyAsync(client, "Customer");
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end, restriction: restriction, state: state);
        if (enrolled) await EnforceAsync(shop);
        if (expired) _clock.Now = end;

        using var preview = await client.GetAsync($"/api/payments/balances?partyType=Customer&partyId={customer}", Ct);
        var limit = (await ReadSuccessAsync(preview)).GetProperty("remainingSettlementDue");
        using var advance = await client.PostAsJsonAsync(PaymentRoute("Customer", customer), Payment(10m), Ct);
        if (limited)
        {
            Assert.Equal(0m, limit.GetDecimal());
            await AssertProblemAsync(advance, HttpStatusCode.Conflict, "Payment.SettlementLimitExceeded");
        }
        else
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, limit.ValueKind);
            Assert.Equal(HttpStatusCode.OK, advance.StatusCode);
        }
    }

    [Fact]
    public async Task Stale_verified_authority_preserves_existing_dues_without_allowing_advances()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var customer = await CreatePartyAsync(client, "Customer");
        await SellOnCreditAsync(client, customer);
        var binding = await BindAsync(shop);
        var verified = _clock.Now.AddHours(-48);
        await ApplyAsync(binding, _clock.Now.AddDays(5), verified: verified);
        await EnforceAsync(shop);

        Assert.Equal(100m, await ReadLimitAsync(client, "Customer", customer));
        var route = PaymentRoute("Customer", customer);
        await AssertProblemAsync(await client.PostAsJsonAsync(route, Payment(100.01m), Ct),
            HttpStatusCode.Conflict, "Payment.SettlementLimitExceeded");
        using var paid = await client.PostAsJsonAsync(route, Payment(100m), Ct);
        await ReadSuccessAsync(paid);
    }

    private async Task InsertUnsupportedBalanceAsync(Guid shop, Guid supplier, decimal amount)
    {
        await using var db = Open(Migrator);
        var actor = await db.ShopMemberships.Where(x => x.ShopId == shop).Select(x => x.UserId).SingleAsync(Ct);
        var adjustment = SupplierLedgerEntry.Create(shop, supplier, null, SupplierLedgerEntryType.RecordAdjusted,
            amount, Payment(1m).PaymentDate, "Legacy ledger only", actor);
        db.SupplierLedgerEntries.Add(adjustment.Value);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task AcceptReturnCreditAsync(HttpClient client, Guid supplier, Guid source)
    {
        using var detail = await client.GetAsync($"/api/purchase-receipts/{source}", Ct);
        var receipt = await ReadSuccessAsync(detail);
        using var accepted = await client.PostAsJsonAsync("/api/supplier-returns", new
        {
            operationKey = Guid.NewGuid().ToString(),
            supplierId = supplier,
            reason = "Damaged goods",
            creditAccepted = true,
            lines = new[] { new { receiptValueId = source, expectedReceiptVersion = receipt.GetProperty("receiptVersion").GetUInt32(),
                expectedBatchVersion = receipt.GetProperty("batchVersion").GetUInt32(), quantity = 3m, proposedCreditAmount = 30m } },
        }, Ct);
        Assert.Equal("CreditAccepted", (await ReadSuccessAsync(accepted)).GetProperty("status").GetString());
    }
}
