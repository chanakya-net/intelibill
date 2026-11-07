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
/// Payment receipts, allocations and reversals against PostgreSQL: the account ledger, each invoice's
/// current outstanding and the receipts' allocated totals must agree after every payment, return and
/// reversal, however they are combined or raced.
/// </summary>
[Collection("Integration Tests")]
public sealed class PaymentAllocationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const decimal InvoiceTotal = 118m;

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

    // ── Task 1: receipts and invoice balances ─────────────────────────────────

    [Fact]
    public async Task PartPaymentThenSettlement_UpdatesCurrentDue()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var saleId = await SellServiceOnCreditAsync(shop, customerId);

        var receipt = await ReadAsync(await PayCustomerAsync(shop, customerId, 30m, Allocate(saleId, 30m), method: "Upi", reference: "UPI-4471"));

        var afterPart = await GetAccountAsync(shop, customerId);
        var invoice = SaleOf(afterPart, saleId);
        Assert.Equal(InvoiceTotal, invoice.GetProperty("dueAmount").GetDecimal());
        Assert.Equal(30m, invoice.GetProperty("allocatedPaymentAmount").GetDecimal());
        Assert.Equal(88m, invoice.GetProperty("currentOutstandingAmount").GetDecimal());
        Assert.Equal(88m, afterPart.GetProperty("outstandingDue").GetDecimal());

        await AssertOkAsync(await PayCustomerAsync(shop, customerId, 88m, Allocate(saleId, 88m)));

        var settled = await GetAccountAsync(shop, customerId);
        var settledInvoice = SaleOf(settled, saleId);
        Assert.Equal(InvoiceTotal, settledInvoice.GetProperty("dueAmount").GetDecimal());
        Assert.Equal(0m, settledInvoice.GetProperty("currentOutstandingAmount").GetDecimal());
        Assert.Equal(0m, settled.GetProperty("outstandingDue").GetDecimal());
        Assert.Equal(0m, settled.GetProperty("unallocatedCredit").GetDecimal());

        var detail = await GetReceiptAsync(shop, receipt.GetProperty("receiptId").GetGuid());
        Assert.Equal("Upi", detail.GetProperty("method").GetString());
        Assert.Equal("UPI-4471", detail.GetProperty("externalReference").GetString());
        Assert.Equal(30m, detail.GetProperty("allocatedAmount").GetDecimal());
        await AssertBalancesReconcileAsync(shop, customerId);
    }

    [Fact]
    public async Task TwoInvoices_ExplicitAllocationRespectsTargets()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var older = await SellServiceOnCreditAsync(shop, customerId);
        var newer = await SellServiceOnCreditAsync(shop, customerId);

        // The operator chose the newer invoice first; nothing reorders it.
        await AssertOkAsync(await PayCustomerAsync(shop, customerId, 150m, Allocate(newer, InvoiceTotal), Allocate(older, 32m)));

        var account = await GetAccountAsync(shop, customerId);
        Assert.Equal(0m, SaleOf(account, newer).GetProperty("currentOutstandingAmount").GetDecimal());
        Assert.Equal(86m, SaleOf(account, older).GetProperty("currentOutstandingAmount").GetDecimal());
        Assert.Equal(86m, account.GetProperty("outstandingDue").GetDecimal());
        await AssertBalancesReconcileAsync(shop, customerId);
    }

    [Fact]
    public async Task Overpayment_RemainsVisibleUnallocatedCredit()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var saleId = await SellServiceOnCreditAsync(shop, customerId);

        await AssertOkAsync(await PayCustomerAsync(shop, customerId, 200m, Allocate(saleId, InvoiceTotal)));

        var account = await GetAccountAsync(shop, customerId);
        Assert.Equal(-82m, account.GetProperty("outstandingDue").GetDecimal());
        Assert.Equal(82m, account.GetProperty("unallocatedCredit").GetDecimal());
        Assert.Equal(0m, SaleOf(account, saleId).GetProperty("currentOutstandingAmount").GetDecimal());

        // A later invoice is not silently settled from the credit.
        var later = await SellServiceOnCreditAsync(shop, customerId);
        var afterSale = await GetAccountAsync(shop, customerId);
        Assert.Equal(InvoiceTotal, SaleOf(afterSale, later).GetProperty("currentOutstandingAmount").GetDecimal());
        Assert.Equal(82m, afterSale.GetProperty("unallocatedCredit").GetDecimal());
        await AssertBalancesReconcileAsync(shop, customerId);
    }

    [Fact]
    public async Task AllocationAboveOutstanding_IsRefusedWithoutAnyEffect()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var saleId = await SellServiceOnCreditAsync(shop, customerId);

        await AssertRefusedAsync(
            await PayCustomerAsync(shop, customerId, 200m, Allocate(saleId, 150m)),
            HttpStatusCode.Conflict,
            "Payment.AllocationExceedsOutstanding");

        Assert.Equal(0, await CountAsync(db => db.PaymentReceipts.CountAsync(r => r.PartyId == customerId)));
        Assert.Equal(0, await CountAsync(db => db.CustomerLedgerEntries.CountAsync(e => e.CustomerId == customerId && e.EntryType == CustomerLedgerEntryType.PaymentReceived)));
    }

    [Fact]
    public async Task CrossPartyOrShopAllocation_IsRefusedWithoutAnyEffect()
    {
        using var shop = await OpenShopAsync();
        var payer = await AddCustomerAsync(shop);
        var other = await AddCustomerAsync(shop);
        var othersInvoice = await SellServiceOnCreditAsync(shop, other);

        await AssertRefusedAsync(
            await PayCustomerAsync(shop, payer, 50m, Allocate(othersInvoice, 50m)),
            HttpStatusCode.NotFound,
            "Payment.AllocationTargetNotFound");
        Assert.Equal(0, await CountAsync(db => db.PaymentReceipts.CountAsync(r => r.PartyId == payer)));
        Assert.Equal(0, await CountAsync(db => db.CustomerLedgerEntries.CountAsync(e => e.CustomerId == payer)));

        // Another shop can neither read the receipt nor allocate it to its own invoices.
        var paid = await ReadAsync(await PayCustomerAsync(shop, other, 20m));
        var receiptId = paid.GetProperty("receiptId").GetGuid();
        using var foreign = await OpenShopAsync();
        var foreignCustomer = await AddCustomerAsync(foreign);
        var foreignInvoice = await SellServiceOnCreditAsync(foreign, foreignCustomer);

        using var read = await SendAsync(foreign, HttpMethod.Get, $"/api/payments/{receiptId}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        await AssertRefusedAsync(
            await SendAsync(foreign, HttpMethod.Post, $"/api/payments/{receiptId}/allocations", new { allocations = new[] { Allocate(foreignInvoice, 10m) } }),
            HttpStatusCode.NotFound,
            "Payment.ReceiptNotFound");
        Assert.Equal(0, await CountAsync(db => db.PaymentAllocations.CountAsync(a => a.ReceiptId == receiptId)));
    }

    [Fact]
    public async Task LaterAllocation_SettlesInvoiceFromUnallocatedCredit()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var saleId = await SellServiceOnCreditAsync(shop, customerId);
        var paid = await ReadAsync(await PayCustomerAsync(shop, customerId, 100m));
        var receiptId = paid.GetProperty("receiptId").GetGuid();

        var before = await GetAccountAsync(shop, customerId);
        Assert.Equal(100m, before.GetProperty("unallocatedCredit").GetDecimal());
        Assert.Equal(InvoiceTotal, SaleOf(before, saleId).GetProperty("currentOutstandingAmount").GetDecimal());

        using var allocated = await SendAsync(shop, HttpMethod.Post, $"/api/payments/{receiptId}/allocations", new
        {
            allocations = new[] { Allocate(saleId, 60m) },
            operationKey = $"alloc-{Guid.NewGuid():N}",
        });
        var body = await ReadAsync(allocated);
        Assert.Equal(40m, body.GetProperty("unallocatedAmount").GetDecimal());

        await AssertRefusedAsync(
            await SendAsync(shop, HttpMethod.Post, $"/api/payments/{receiptId}/allocations", new { allocations = new[] { Allocate(saleId, 50m) } }),
            HttpStatusCode.Conflict,
            "Payment.AllocationExceedsReceipt");

        var after = await GetAccountAsync(shop, customerId);
        Assert.Equal(58m, SaleOf(after, saleId).GetProperty("currentOutstandingAmount").GetDecimal());
        Assert.Equal(40m, after.GetProperty("unallocatedCredit").GetDecimal());
        await AssertBalancesReconcileAsync(shop, customerId);
    }

    [Fact]
    public async Task SupplierPayment_AllocatesToPurchaseReceiptLiability()
    {
        using var shop = await OpenShopAsync();
        var supplierId = await AddSupplierAsync(shop);
        var receiptValueId = await ReceiveStockAsync(shop, supplierId, 800m);

        var paid = await SendAsync(shop, HttpMethod.Post, $"/api/suppliers/{supplierId}/payments", new
        {
            amount = 500m,
            paymentDate = Today,
            notes = "Part payment",
            operationKey = $"sup-{Guid.NewGuid():N}",
            method = "BankTransfer",
            allocations = new[] { Allocate(receiptValueId, 500m) },
        });
        await AssertOkAsync(paid);

        var balances = await GetBalancesAsync(shop, "Supplier", supplierId);
        var receipt = balances.GetProperty("purchaseReceipts").EnumerateArray().Single(r => r.GetProperty("purchaseReceiptValueId").GetGuid() == receiptValueId);
        Assert.Equal(800m, receipt.GetProperty("payableAmount").GetDecimal());
        Assert.Equal(300m, receipt.GetProperty("outstandingAmount").GetDecimal());

        // The payable can no longer be voided or corrected below what payments settled.
        var batchId = await QueryAsync(db => db.PurchaseReceiptValues.Where(r => r.Id == receiptValueId).Select(r => r.InventoryBatchId).SingleAsync());
        await AssertRefusedAsync(
            await SendAsync(shop, HttpMethod.Post, $"/api/inventory/batches/{batchId}/void", new { }),
            HttpStatusCode.Conflict,
            "Payment.DocumentHasAllocations");
    }

    [Fact]
    public async Task AllocationToPurchaseReceipt_InvalidatesAStaleCorrectionAndFloorsItAtAllocated()
    {
        using var shop = await OpenShopAsync();
        var supplierId = await AddSupplierAsync(shop);
        var receiptValueId = await ReceiveStockAsync(shop, supplierId, 800m);
        var versionBefore = await QueryAsync(db => db.PurchaseReceiptValues.Where(r => r.Id == receiptValueId).Select(r => r.Version).SingleAsync());

        await AssertOkAsync(await SendAsync(shop, HttpMethod.Post, $"/api/suppliers/{supplierId}/payments", new
        {
            amount = 500m,
            paymentDate = Today,
            notes = (string?)null,
            operationKey = $"sup-{Guid.NewGuid():N}",
            allocations = new[] { Allocate(receiptValueId, 500m) },
        }));

        // A correction prepared before the allocation (or a void/reassignment that read the row
        // before it) is now stale: the allocation moved the row version.
        await AssertRefusedAsync(
            await SendAsync(shop, HttpMethod.Post, $"/api/inventory/receipts/{receiptValueId}/cost-corrections", new
            {
                expectedVersion = versionBefore,
                revisedTotalCost = 600m,
                reason = "Supplier invoice",
                operationKey = (string?)null,
            }),
            HttpStatusCode.Conflict,
            "PurchaseReceipt.VersionConflict");

        var current = await QueryAsync(db => db.PurchaseReceiptValues.Where(r => r.Id == receiptValueId).Select(r => r.Version).SingleAsync());
        await AssertRefusedAsync(
            await SendAsync(shop, HttpMethod.Post, $"/api/inventory/receipts/{receiptValueId}/cost-corrections", new
            {
                expectedVersion = current,
                revisedTotalCost = 400m,
                reason = "Supplier invoice",
                operationKey = (string?)null,
            }),
            HttpStatusCode.Conflict,
            "Payment.CorrectionBelowAllocated");
    }

    [Fact]
    public async Task ReverseSupplierPayment_VoidsTheCorrectedExpenseToo()
    {
        using var shop = await OpenShopAsync();
        var supplierId = await AddSupplierAsync(shop);
        var paid = await ReadAsync(await SendAsync(shop, HttpMethod.Post, $"/api/suppliers/{supplierId}/payments", new
        {
            amount = 300m,
            paymentDate = Today,
            notes = (string?)null,
            operationKey = $"sup-{Guid.NewGuid():N}",
        }));
        var receiptId = paid.GetProperty("receiptId").GetGuid();
        var expenseId = (await QueryAsync(db => db.PaymentReceipts.Where(r => r.Id == receiptId).Select(r => r.ExpenseId).SingleAsync()))!.Value;

        await AssertOkAsync(await SendAsync(shop, HttpMethod.Post, $"/api/expenses/{expenseId}/correct", new
        {
            categoryName = "Supplier Payments",
            amount = 280m,
            paidTo = "Supplier",
            description = "Corrected amount",
            expenseDate = Today,
        }));

        await AssertOkAsync(await ReverseAsync(shop, receiptId, "Cheque bounced", $"rev-{Guid.NewGuid():N}"));

        var active = await QueryAsync(db => db.Expenses.CountAsync(e => !e.IsVoided && (e.Id == expenseId || e.OriginalExpenseId == expenseId)));
        Assert.Equal(0, active);
    }

    // ── Task 2: corrections and races ─────────────────────────────────────────

    [Fact]
    public async Task ReverseAllocatedReceipt_RestoresOutstandingOnce()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var saleId = await SellServiceOnCreditAsync(shop, customerId);
        var paid = await ReadAsync(await PayCustomerAsync(shop, customerId, 30m, Allocate(saleId, 30m), method: "Cash"));
        var receiptId = paid.GetProperty("receiptId").GetGuid();

        var reversed = await ReadAsync(await ReverseAsync(shop, receiptId, "Recorded against the wrong customer", $"rev-{Guid.NewGuid():N}"));
        Assert.Equal("Recorded against the wrong customer", reversed.GetProperty("reversal").GetProperty("reason").GetString());
        Assert.Equal(0m, reversed.GetProperty("allocatedAmount").GetDecimal());

        var account = await GetAccountAsync(shop, customerId);
        Assert.Equal(InvoiceTotal, SaleOf(account, saleId).GetProperty("currentOutstandingAmount").GetDecimal());
        Assert.Equal(InvoiceTotal, account.GetProperty("outstandingDue").GetDecimal());

        // The original receipt and its allocation are kept unchanged; exactly one reversal exists.
        var original = await QueryAsync(db => db.PaymentReceipts.AsNoTracking().SingleAsync(r => r.Id == receiptId));
        Assert.Equal(30m, original.Amount);
        Assert.Equal(ReceiptTenderMethod.Cash, original.Method);
        Assert.Equal(1, await CountAsync(db => db.PaymentAllocations.CountAsync(a => a.ReceiptId == receiptId)));
        Assert.Equal(1, await CountAsync(db => db.PaymentReversals.CountAsync(r => r.ReceiptId == receiptId)));
        Assert.Equal(1, await CountAsync(db => db.CustomerLedgerEntries.CountAsync(e => e.CustomerId == customerId && e.EntryType == CustomerLedgerEntryType.PaymentReversal)));

        await AssertRefusedAsync(
            await ReverseAsync(shop, receiptId, "Second attempt", $"rev-{Guid.NewGuid():N}"),
            HttpStatusCode.Conflict,
            "Payment.AlreadyReversed");
        await AssertRefusedAsync(
            await SendAsync(shop, HttpMethod.Post, $"/api/payments/{receiptId}/allocations", new { allocations = new[] { Allocate(saleId, 10m) } }),
            HttpStatusCode.Conflict,
            "Payment.ReversedReceiptCannotBeAllocated");
        await AssertBalancesReconcileAsync(shop, customerId);
    }

    [Fact]
    public async Task ReverseSupplierPayment_VoidsLinkedExpenseAndRestoresPayable()
    {
        using var shop = await OpenShopAsync();
        var supplierId = await AddSupplierAsync(shop);
        var receiptValueId = await ReceiveStockAsync(shop, supplierId, 800m);
        var paid = await ReadAsync(await SendAsync(shop, HttpMethod.Post, $"/api/suppliers/{supplierId}/payments", new
        {
            amount = 300m,
            paymentDate = Today,
            notes = (string?)null,
            operationKey = $"sup-{Guid.NewGuid():N}",
            allocations = new[] { Allocate(receiptValueId, 300m) },
        }));
        var receiptId = paid.GetProperty("receiptId").GetGuid();
        var expenseId = await QueryAsync(db => db.PaymentReceipts.Where(r => r.Id == receiptId).Select(r => r.ExpenseId).SingleAsync());
        Assert.NotNull(expenseId);

        await AssertOkAsync(await ReverseAsync(shop, receiptId, "Cheque bounced", $"rev-{Guid.NewGuid():N}"));

        Assert.True(await QueryAsync(db => db.Expenses.Where(e => e.Id == expenseId).Select(e => e.IsVoided).SingleAsync()));
        var balances = await GetBalancesAsync(shop, "Supplier", supplierId);
        var receipt = balances.GetProperty("purchaseReceipts").EnumerateArray().Single(r => r.GetProperty("purchaseReceiptValueId").GetGuid() == receiptValueId);
        Assert.Equal(800m, receipt.GetProperty("outstandingAmount").GetDecimal());
        var ledgerBalance = await QueryAsync(async db =>
        {
            var entries = await db.SupplierLedgerEntries.Where(e => e.SupplierId == supplierId).ToListAsync();
            return entries.Sum(e => e.EntryType == SupplierLedgerEntryType.PaymentMade ? -e.Amount : e.Amount);
        });
        Assert.Equal(800m, ledgerBalance);
    }

    [Fact]
    public async Task DuplicateReversal_ReplaysOrConflicts()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var paid = await ReadAsync(await PayCustomerAsync(shop, customerId, 40m));
        var receiptId = paid.GetProperty("receiptId").GetGuid();
        var key = $"rev-{Guid.NewGuid():N}";

        var first = await ReadAsync(await ReverseAsync(shop, receiptId, "Duplicate entry", key));
        var replay = await ReadAsync(await ReverseAsync(shop, receiptId, "Duplicate entry", key));
        Assert.Equal(
            first.GetProperty("reversal").GetProperty("reversalId").GetGuid(),
            replay.GetProperty("reversal").GetProperty("reversalId").GetGuid());

        await AssertRefusedAsync(await ReverseAsync(shop, receiptId, "Another reason", key), HttpStatusCode.Conflict, "Operation.PayloadMismatch");

        // Two different keys racing: exactly one reversal commits.
        var second = await ReadAsync(await PayCustomerAsync(shop, customerId, 25m));
        var racedId = second.GetProperty("receiptId").GetGuid();
        var raced = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => Task.Run(async () =>
        {
            using var client = await shop.CloneAsync(_factory);
            return await ReverseAsync(client, racedId, "Raced reversal", $"race-{i}-{Guid.NewGuid():N}");
        })));
        Assert.Equal(1, raced.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(raced.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await CountAsync(db => db.PaymentReversals.CountAsync(r => r.ReceiptId == racedId)));
        Assert.Equal(2, await CountAsync(db => db.CustomerLedgerEntries.CountAsync(e => e.CustomerId == customerId && e.EntryType == CustomerLedgerEntryType.PaymentReversal)));
    }

    [Fact]
    public async Task ConcurrentCollections_CannotOverAllocate()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var saleId = await SellServiceOnCreditAsync(shop, customerId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            using var client = await shop.CloneAsync(_factory);
            return await PayCustomerAsync(client, customerId, 100m, Allocate(saleId, 100m));
        })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var allocated = await QueryAsync(db => db.PaymentAllocations.Where(a => a.TargetId == saleId).SumAsync(a => a.Amount));
        Assert.Equal(100m, allocated);
        Assert.Equal(1, await CountAsync(db => db.PaymentReceipts.CountAsync(r => r.PartyId == customerId)));
        await AssertBalancesReconcileAsync(shop, customerId);
    }

    [Fact]
    public async Task PaymentVersusReturn_UsesCurrentOutstanding()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var settled = await SellServiceOnCreditAsync(shop, customerId);
        var open = await SellServiceOnCreditAsync(shop, customerId);

        // The first invoice is settled by an allocated payment: returning it cannot also cut the
        // account's debt, which belongs to the second invoice. The refund goes out instead.
        await AssertOkAsync(await PayCustomerAsync(shop, customerId, InvoiceTotal, Allocate(settled, InvoiceTotal)));
        await AssertOkAsync(await ReturnServiceAsync(shop, settled));

        var dueReduction = await QueryAsync(db => db.SaleReturns.Where(r => r.SaleId == settled).SumAsync(r => r.DueReductionAmount));
        Assert.Equal(0m, dueReduction);
        var account = await GetAccountAsync(shop, customerId);
        Assert.Equal(InvoiceTotal, account.GetProperty("outstandingDue").GetDecimal());
        Assert.Equal(InvoiceTotal, SaleOf(account, open).GetProperty("currentOutstandingAmount").GetDecimal());

        // Raced: a payment allocating the whole open invoice and a return of it. Whichever commits
        // first, the other sees what it left: the invoice is never reduced twice.
        var raced = await Task.WhenAll(
            Task.Run(async () =>
            {
                using var client = await shop.CloneAsync(_factory);
                return await PayCustomerAsync(client, customerId, InvoiceTotal, Allocate(open, InvoiceTotal));
            }),
            Task.Run(async () =>
            {
                using var client = await shop.CloneAsync(_factory);
                return await ReturnServiceAsync(client, open);
            }));
        Assert.Contains(raced, r => r.StatusCode == HttpStatusCode.OK);

        var returned = await QueryAsync(db => db.SaleReturns.Where(r => r.SaleId == open && !r.IsVoided).SumAsync(r => r.DueReductionAmount));
        var paidToInvoice = await QueryAsync(db => db.PaymentAllocations.Where(a => a.TargetId == open).SumAsync(a => a.Amount));
        Assert.True(returned + paidToInvoice <= InvoiceTotal, $"returned {returned} + allocated {paidToInvoice}");
        await AssertBalancesReconcileAsync(shop, customerId);
    }

    [Fact]
    public async Task LegacyAccount_PaymentWithoutAllocationLeavesInvoiceDueButBalanceCorrect()
    {
        using var shop = await OpenShopAsync();
        var customerId = await AddCustomerAsync(shop);
        var saleId = await SellServiceOnCreditAsync(shop, customerId);

        // An older client sends no tender: the receipt says so instead of guessing cash.
        await AssertOkAsync(await SendAsync(shop, HttpMethod.Post, $"/api/customers/{customerId}/payments", new
        {
            amount = 50m,
            paymentDate = Today,
            notes = (string?)null,
        }));

        var receipt = await QueryAsync(db => db.PaymentReceipts.AsNoTracking().SingleAsync(r => r.PartyId == customerId));
        Assert.Equal(ReceiptTenderMethod.UnknownRecordedMethod, receipt.Method);
        var account = await GetAccountAsync(shop, customerId);
        Assert.Equal(68m, account.GetProperty("outstandingDue").GetDecimal());
        Assert.Equal(InvoiceTotal, SaleOf(account, saleId).GetProperty("currentOutstandingAmount").GetDecimal());
        Assert.Equal(50m, account.GetProperty("unallocatedCredit").GetDecimal());
        await AssertBalancesReconcileAsync(shop, customerId);
    }

    // ── Invariant ─────────────────────────────────────────────────────────────

    /// <summary>Ledger balance = Σ invoice outstanding − unallocated payment credit.</summary>
    private static async Task AssertBalancesReconcileAsync(Shop shop, Guid customerId)
    {
        var account = await GetAccountAsync(shop, customerId);
        var outstanding = account.GetProperty("sales").EnumerateArray().Sum(s => s.GetProperty("currentOutstandingAmount").GetDecimal());
        var credit = account.GetProperty("unallocatedCredit").GetDecimal();
        Assert.Equal(account.GetProperty("outstandingDue").GetDecimal(), outstanding - credit);
    }

    // ── Seeding and requests ──────────────────────────────────────────────────

    private static string Today => BusinessDates.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

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

    private static object Allocate(Guid targetId, decimal amount) => new { targetId, amount };

    private async Task<Shop> OpenShopAsync()
    {
        var client = _factory.CreateClient(ClientOptions);
        var enrolled = await client.EnrollAsync(new
        {
            email = $"payments-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Pay",
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

    private static async Task<Guid> AddCustomerAsync(Shop shop)
    {
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/customers", new
        {
            name = "Credit Customer",
            phoneNumber = $"+9198{Random.Shared.Next(10_000_000, 99_999_999)}",
            address = (string?)null,
            isActive = true,
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("customerId").GetGuid();
    }

    private static async Task<Guid> AddSupplierAsync(Shop shop)
    {
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/suppliers", new
        {
            name = $"Supplier {Guid.NewGuid():N}"[..20],
            contactPersonName = (string?)null,
            contactPersonPhone = (string?)null,
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pin = "560001",
            isActive = true,
            isPreferred = false,
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        var body = JsonSerializer.Deserialize<JsonElement>(content);
        return (body.TryGetProperty("supplierId", out var id) ? id : body.GetProperty("id")).GetGuid();
    }

    private async Task<Guid> ReceiveStockAsync(Shop shop, Guid supplierId, decimal totalCost)
    {
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/inventory/inbound", new
        {
            itemName = "Payable Item",
            barcode = $"PAY-{Guid.NewGuid():N}",
            itemDescription = (string?)null,
            hsnCode = (string?)null,
            uom = "pcs",
            batchNumber = "B-001",
            quantity = 10m,
            totalPurchaseCost = totalCost,
            mrp = 120m,
            salesPrice = 100m,
            taxRatePercent = 18m,
            taxIncluded = false,
            purchaseTaxIncluded = false,
            expiryDate = (DateOnly?)null,
            manufacturingDate = (DateOnly?)null,
            supplierId,
            referenceNumber = (string?)null,
            notes = (string?)null,
            performedAt = (DateTimeOffset?)null,
        });
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        var batchId = JsonSerializer.Deserialize<JsonElement>(content).GetProperty("inventoryBatchId").GetGuid();
        return await QueryAsync(db => db.PurchaseReceiptValues.Where(r => r.InventoryBatchId == batchId).Select(r => r.Id).SingleAsync());
    }

    private static async Task<Guid> AddServiceAsync(Shop shop)
    {
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/services", new
        {
            name = $"Service {Guid.NewGuid():N}"[..20],
            description = "Payment test service",
            price = 100m,
            hsnCode = "9987",
            taxRatePercent = 18m,
            taxIncluded = false,
            isActive = true,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("serviceId").GetGuid();
    }

    private static async Task<Guid> SellServiceOnCreditAsync(Shop shop, Guid customerId)
    {
        var serviceId = await AddServiceAsync(shop);
        using var response = await SendAsync(shop, HttpMethod.Post, "/api/sales", new
        {
            idempotencyKey = $"sale-{Guid.NewGuid():N}",
            customerId,
            customerName = "Credit Customer",
            customerPhone = "+919812345678",
            paymentMethod = (int)PaymentMethod.Credit,
            paidAmount = 0m,
            dueAmount = InvoiceTotal,
            items = new[]
            {
                new
                {
                    barcode = "SVC-PAY",
                    batchNumber = string.Empty,
                    itemName = "Payment Service",
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
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, content);
        return JsonSerializer.Deserialize<JsonElement>(content).GetProperty("saleId").GetGuid();
    }

    private async Task<HttpResponseMessage> ReturnServiceAsync(Shop shop, Guid saleId)
    {
        var lineId = await QueryAsync(db => db.SaleItems.Where(i => i.SaleId == saleId).Select(i => i.Id).SingleAsync());
        return await SendAsync(shop, HttpMethod.Post, $"/api/sales/{saleId}/returns", new
        {
            payoutDestination = (int)ReturnPayoutDestination.Refund,
            dueReductionOverrideAmount = (decimal?)null,
            dueOverrideReason = (string?)null,
            notes = "Payment race return",
            items = new[]
            {
                new
                {
                    saleItemId = lineId,
                    quantity = 1m,
                    lineType = (int)SaleLineType.Service,
                    condition = (int?)null,
                    approvedRefundAmount = InvoiceTotal,
                    notes = (string?)null,
                },
            },
        });
    }

    private static Task<HttpResponseMessage> PayCustomerAsync(
        Shop shop,
        Guid customerId,
        decimal amount,
        params object[] allocations) =>
        PayCustomerAsync(shop, customerId, amount, allocations, method: null, reference: null);

    private static Task<HttpResponseMessage> PayCustomerAsync(
        Shop shop,
        Guid customerId,
        decimal amount,
        object allocation,
        string? method,
        string? reference = null) =>
        PayCustomerAsync(shop, customerId, amount, [allocation], method, reference);

    private static Task<HttpResponseMessage> PayCustomerAsync(
        Shop shop,
        Guid customerId,
        decimal amount,
        object[] allocations,
        string? method,
        string? reference) =>
        SendAsync(shop, HttpMethod.Post, $"/api/customers/{customerId}/payments", new
        {
            amount,
            paymentDate = Today,
            notes = "Collected",
            operationKey = $"pay-{Guid.NewGuid():N}",
            method,
            externalReference = reference,
            allocations,
        });

    private static Task<HttpResponseMessage> ReverseAsync(Shop shop, Guid receiptId, string reason, string key) =>
        SendAsync(shop, HttpMethod.Post, $"/api/payments/{receiptId}/reversal", new { reason, operationKey = key });

    private static async Task<JsonElement> GetAccountAsync(Shop shop, Guid customerId)
    {
        using var response = await SendAsync(shop, HttpMethod.Get, $"/api/customers/{customerId}/account");
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> GetReceiptAsync(Shop shop, Guid receiptId)
    {
        using var response = await SendAsync(shop, HttpMethod.Get, $"/api/payments/{receiptId}");
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> GetBalancesAsync(Shop shop, string partyType, Guid partyId)
    {
        using var response = await SendAsync(shop, HttpMethod.Get, $"/api/payments/balances?partyType={partyType}&partyId={partyId}");
        return await ReadAsync(response);
    }

    private static JsonElement SaleOf(JsonElement account, Guid saleId) =>
        account.GetProperty("sales").EnumerateArray().Single(s => s.GetProperty("saleId").GetGuid() == saleId);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {content}");
        return JsonSerializer.Deserialize<JsonElement>(content);
    }

    private static async Task AssertOkAsync(HttpResponseMessage response)
    {
        using (response)
            await ReadAsync(response);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status, $"{response.StatusCode}: {content}");
            Assert.Contains(code, content, StringComparison.Ordinal);
        }
    }

    private async Task<T> QueryAsync<T>(Func<ApplicationDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private Task<int> CountAsync(Func<ApplicationDbContext, Task<int>> query) => QueryAsync(query);
}
