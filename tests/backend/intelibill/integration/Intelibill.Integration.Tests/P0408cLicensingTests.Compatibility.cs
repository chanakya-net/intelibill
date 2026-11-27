using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408cLicensingTests
{
    [Theory]
    [InlineData("Expired")]
    [InlineData("Trading")]
    [InlineData("PendingActivation")]
    public async Task Existing_supplier_obligations_remain_accessible(string phase)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var staff = CreateClient(host);
        using var foreign = CreateClient(host);
        var binding = await OpenTradingAsync(owner);
        var shop = binding.Shop;
        await CreateShopAsync(foreign);
        await SignInStaffAsync(owner, staff, shop);
        var supplier = await AddSupplierIdAsync(owner, "Existing supplier");
        var payable = await ReceiveStockAsync(owner, supplier, 800m);
        var returnSource = await ReceiveStockAsync(owner, supplier, 1000m);
        var payment = new
        {
            amount = 500m, paymentDate = BusinessCalendar.DateOf(_clock.Now),
            operationKey = $"supplier-test-{Guid.NewGuid():N}", method = "BankTransfer",
            allocations = new[] { new { targetId = payable, amount = 500m } },
        };

        await RestrictAsync(binding, phase);
        await AssertSupplierReadsAsync(owner, supplier);
        using var staffDirectory = await staff.GetAsync("/api/suppliers/directory", Ct);
        staffDirectory.EnsureSuccessStatusCode();
        using var staffPayment = await staff.PostAsJsonAsync($"/api/suppliers/{supplier}/payments", payment, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, staffPayment.StatusCode);
        using var staffCreate = await CreateSupplierAsync(staff, "Staff supplier");
        Assert.Equal(HttpStatusCode.Forbidden, staffCreate.StatusCode);
        await AssertProblemAsync(await foreign.GetAsync($"/api/suppliers/{supplier}", Ct), HttpStatusCode.NotFound,
            "Supplier.SupplierNotFound");
        await AssertProblemAsync(await foreign.PostAsJsonAsync($"/api/suppliers/{supplier}/payments", payment, Ct),
            HttpStatusCode.NotFound, "Supplier.SupplierNotFound");
        await AssertProblemAsync(await foreign.PutAsJsonAsync($"/api/suppliers/{supplier}", SupplierInput("Foreign edit", true), Ct),
            HttpStatusCode.NotFound, "Supplier.SupplierNotFound");

        using var paid = await owner.PostAsJsonAsync($"/api/suppliers/{supplier}/payments", payment, Ct);
        using var replay = await owner.PostAsJsonAsync($"/api/suppliers/{supplier}/payments", payment, Ct);
        var ledger = await ReadSuccessAsync(paid);
        var receipt = ledger.GetProperty("receiptId").GetGuid();
        Assert.Equal(ledger.GetProperty("id").GetGuid(), (await ReadSuccessAsync(replay)).GetProperty("id").GetGuid());
        using var details = await owner.GetAsync($"/api/purchase-receipts/{payable}", Ct);
        Assert.Equal(300m, (await ReadSuccessAsync(details)).GetProperty("outstandingAmount").GetDecimal());
        await ShipReturnAsync(owner, supplier, returnSource);
        await AssertSettledFactsAsync(shop, supplier, payable, receipt);

        if (phase == "Trading")
            await AssertSecurityScopeAsync(owner, foreign, binding, supplier, payment);
    }

    private async Task RestrictAsync(Binding binding, string phase)
    {
        if (phase == "Expired")
            _clock.Now = _clock.Now.AddDays(1);
        else
            await ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction: phase == "Trading" ? phase : "Unrestricted",
                state: phase == "Trading" ? "Active" : phase);
    }

    private static async Task AssertSupplierReadsAsync(HttpClient client, Guid supplier)
    {
        using var list = await client.GetAsync("/api/suppliers?include_system=false", Ct);
        Assert.Equal(supplier, Assert.Single((await ReadSuccessAsync(list)).EnumerateArray()).GetProperty("supplierId").GetGuid());
        using var directory = await client.GetAsync("/api/suppliers/directory?include_system=false", Ct);
        Assert.Equal(supplier, Assert.Single((await ReadSuccessAsync(directory)).GetProperty("items").EnumerateArray())
            .GetProperty("supplierId").GetGuid());
        using var detail = await client.GetAsync($"/api/suppliers/{supplier}", Ct);
        Assert.Equal(supplier, (await ReadSuccessAsync(detail)).GetProperty("supplierId").GetGuid());
        using var ledger = await client.GetAsync($"/api/suppliers/{supplier}/ledger", Ct);
        Assert.Equal(2, (await ReadSuccessAsync(ledger)).GetArrayLength());
    }

    private async Task AssertSecurityScopeAsync(HttpClient affected, HttpClient foreign, Binding binding, Guid supplier, object payment)
    {
        await ApplyAsync(binding, _clock.Now.AddDays(1), 3, restriction: "Security");
        await AssertProblemAsync(await affected.GetAsync("/api/suppliers/directory", Ct), HttpStatusCode.Forbidden,
            "Licence.SecuritySuspended");
        await AssertProblemAsync(await affected.PostAsJsonAsync($"/api/suppliers/{supplier}/payments", payment, Ct),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        using var unrelated = await foreign.GetAsync("/api/suppliers/directory", Ct);
        unrelated.EnsureSuccessStatusCode();
    }

    private async Task AssertSettledFactsAsync(Guid shop, Guid supplier, Guid payable, Guid receipt)
    {
        await using var db = Open(Migrator);
        Assert.Equal(800m, await db.PurchaseReceiptValues.Where(x => x.Id == payable).Select(x => x.OriginalTotalCost).SingleAsync(Ct));
        Assert.Equal(receipt, await db.PaymentReceipts.Where(x => x.ShopId == shop && x.PartyId == supplier)
            .Select(x => x.Id).SingleAsync(Ct));
        var allocation = await db.PaymentAllocations.SingleAsync(x => x.ReceiptId == receipt, Ct);
        Assert.Equal(payable, allocation.TargetId);
        Assert.Equal(500m, allocation.Amount);
        Assert.Equal(1, await db.SupplierLedgerEntries.CountAsync(x =>
            x.SupplierId == supplier && x.EntryType == SupplierLedgerEntryType.PaymentMade, Ct));
        Assert.Equal(1, await db.SupplierReturns.CountAsync(x => x.ShopId == shop && x.SupplierId == supplier, Ct));
        Assert.Equal("Existing supplier", Assert.Single(await SupplierNamesAsync(shop)));
        _output.WriteLine("PostgreSQL retained original 800.00 payable, one 500.00 allocation and one source-linked return.");
    }

    private static async Task ShipReturnAsync(HttpClient client, Guid supplier, Guid receipt)
    {
        using var detail = await client.GetAsync($"/api/purchase-receipts/{receipt}", Ct);
        var source = await ReadSuccessAsync(detail);
        using var response = await client.PostAsJsonAsync("/api/supplier-returns", new
        {
            supplierId = supplier, operationKey = Guid.NewGuid().ToString(), reason = "Damaged packaging",
            creditAccepted = false, lines = new[] { new
            {
                receiptValueId = receipt, expectedReceiptVersion = source.GetProperty("receiptVersion").GetUInt32(),
                expectedBatchVersion = source.GetProperty("batchVersion").GetUInt32(), quantity = 3m, proposedCreditAmount = 300m,
            } },
        }, Ct);
        var shipment = await ReadSuccessAsync(response);
        Assert.NotEqual(Guid.Empty, shipment.GetProperty("returnId").GetGuid());
    }

    private async Task<Guid> ReceiveStockAsync(HttpClient client, Guid supplier, decimal cost)
    {
        using var response = await client.PostAsJsonAsync("/api/inventory/inbound", InboundInput($"Payable {cost}", supplier, cost), Ct);
        var batch = (await ReadSuccessAsync(response)).GetProperty("inventoryBatchId").GetGuid();
        await using var db = Open(Migrator);
        return await db.PurchaseReceiptValues.Where(x => x.InventoryBatchId == batch).Select(x => x.Id).SingleAsync(Ct);
    }

    private static async Task<Guid> AddSupplierIdAsync(HttpClient client, string name)
    {
        using var created = await CreateSupplierAsync(client, name);
        return (await ReadSuccessAsync(created)).GetProperty("supplierId").GetGuid();
    }

    private static async Task SignInStaffAsync(HttpClient owner, HttpClient staff, Guid shop)
    {
        var email = $"p0408c-staff-{Guid.NewGuid():N}@fixture.test";
        const string password = "Pass123!Aa";
        using var seed = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        seed.Headers.Authorization = owner.DefaultRequestHeaders.Authorization;
        seed.Content = JsonContent.Create(new
        {
            shopIds = new[] { shop }, email, password, firstName = "Test", lastName = "Staff",
            confirmPassword = password, phoneNumber = "+919876543211", role = "Staff",
        });
        using var seeded = await owner.SeedVerifiedStaffAsync(seed);
        seeded.EnsureSuccessStatusCode();
        using var login = await staff.PostAsJsonAsync("/api/auth/login", new { identifier = email, password }, Ct);
        staff.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(login)).GetProperty("accessToken").GetString());
    }
}
