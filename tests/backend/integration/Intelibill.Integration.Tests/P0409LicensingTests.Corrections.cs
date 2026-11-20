using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0409LicensingTests
{
    [Fact]
    public async Task Restricted_shop_keeps_an_eligible_correction_but_refuses_one_that_adds_an_expense()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(5);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        var date = DateOnly.FromDateTime(_clock.Now.UtcDateTime);
        using var created = await CreateExpenseAsync(client);
        var original = (await ReadSuccessAsync(created)).GetProperty("id").GetGuid();
        _clock.Now = end;

        using var unrelated = await client.PostAsJsonAsync($"/api/expenses/{original}/correct", new
        {
            categoryName = "Rent", amount = 60m, paidTo = "Unrelated payee", expenseDate = date,
            operationKey = Guid.NewGuid().ToString(),
        }, Ct);
        await AssertProblemAsync(unrelated, HttpStatusCode.Conflict, "Expense.CorrectionSourceIneligible");
        using var larger = await CorrectAsync(client, original, 150m, date);
        await AssertProblemAsync(larger, HttpStatusCode.Conflict, "Expense.CorrectionSourceIneligible");
        using var moved = await CorrectAsync(client, original, 100m, date.AddDays(1));
        await AssertProblemAsync(moved, HttpStatusCode.Conflict, "Expense.CorrectionSourceIneligible");
        await using (var db = Open(Migrator))
        {
            Assert.Equal(1, await db.Expenses.CountAsync(x => x.ShopId == shop, Ct));
            Assert.False((await db.Expenses.SingleAsync(x => x.Id == original, Ct)).IsVoided);
            Assert.Equal(0, await db.OperationReceipts.CountAsync(x => x.ShopId == shop, Ct));
        }

        using var eligible = await CorrectAsync(client, original, 60m, date);
        Assert.Equal(HttpStatusCode.OK, eligible.StatusCode);
        await using var after = Open(Migrator);
        Assert.Equal(2, await after.Expenses.CountAsync(x => x.ShopId == shop, Ct));
        Assert.True((await after.Expenses.SingleAsync(x => x.Id == original, Ct)).IsVoided);
        using var fresh = await CreateExpenseAsync(client);
        await AssertProblemAsync(fresh, HttpStatusCode.Forbidden, "Licence.Expired");
    }

    [Fact]
    public async Task Restricted_correction_preserves_supplier_payment_and_rejects_foreign_sources()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var owner = CreateClient(host);
        using var foreign = CreateClient(host);
        var shop = await CreateShopAsync(owner);
        var otherShop = await CreateShopAsync(foreign);
        var binding = await BindAsync(shop);
        await ApplyAsync(binding, _clock.Now.AddMinutes(1));
        await EnforceAsync(shop);
        var otherBinding = await BindAsync(otherShop);
        await ApplyAsync(otherBinding, _clock.Now.AddMinutes(1));
        await EnforceAsync(otherShop);
        using var supplierResponse = await owner.PostAsJsonAsync("/api/suppliers", new
        {
            name = "Supplier", isActive = true, isPreferred = true, contactPersonName = "Sales",
            contactPersonPhone = "+919111111111", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pin = "560001",
        }, Ct);
        var supplier = (await ReadSuccessAsync(supplierResponse)).GetProperty("supplierId").GetGuid();
        var date = DateOnly.FromDateTime(_clock.Now.UtcDateTime);
        using var payment = await owner.PostAsJsonAsync($"/api/suppliers/{supplier}/payments", new
        {
            amount = 100m, paymentDate = date, method = "Cash", operationKey = Guid.NewGuid().ToString(),
        }, Ct);
        var receiptId = (await ReadSuccessAsync(payment)).GetProperty("receiptId").GetGuid();
        Guid expenseId;
        string before;
        await using (var db = Open(Migrator))
        {
            expenseId = (await db.PaymentReceipts.SingleAsync(x => x.Id == receiptId, Ct)).ExpenseId!.Value;
            before = await SupplierFactsAsync(db, shop, expenseId, receiptId);
        }
        _clock.Now = _clock.Now.AddMinutes(1);
        await AssertProblemAsync(await CorrectAsync(owner, expenseId, 60m, date), HttpStatusCode.BadRequest, "Expense.SupplierSettlementCorrectionRefused");
        await AssertProblemAsync(await CorrectAsync(foreign, expenseId, 60m, date), HttpStatusCode.NotFound, "Expense.NotFound");
        await using var after = Open(Migrator);
        Assert.False((await after.Expenses.SingleAsync(x => x.Id == expenseId, Ct)).IsVoided);
        Assert.Equal(1, await after.Expenses.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(0, await after.Expenses.CountAsync(x => x.ShopId == otherShop, Ct));
        Assert.Equal(1, await after.PaymentReceipts.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(1, await after.SupplierLedgerEntries.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(before, await SupplierFactsAsync(after, shop, expenseId, receiptId));
    }

    private static async Task<string> SupplierFactsAsync(Intelibill.Infrastructure.Data.ApplicationDbContext db, Guid shop, Guid expenseId, Guid receiptId)
    {
        var expense = await db.Expenses.AsNoTracking().Where(x => x.Id == expenseId)
            .Select(x => new { x.Id, x.Amount, x.PaidTo, x.Description, x.ExpenseDate, x.IsVoided, x.Treatment, x.SupplierLedgerEntryId }).SingleAsync(Ct);
        var receipt = await db.PaymentReceipts.AsNoTracking().Where(x => x.Id == receiptId)
            .Select(x => new { x.Id, x.Amount, x.LedgerEntryId, x.ExpenseId, x.Method, x.BusinessDate }).SingleAsync(Ct);
        var ledger = await db.SupplierLedgerEntries.AsNoTracking().Where(x => x.ShopId == shop)
            .Select(x => new { x.Id, x.SupplierId, x.Amount, x.EntryType, x.EntryDate, x.Notes }).ToArrayAsync(Ct);
        return System.Text.Json.JsonSerializer.Serialize(new { expense, receipt, ledger });
    }

    private static Task<HttpResponseMessage> CorrectAsync(HttpClient client, Guid id, decimal amount, DateOnly date) =>
        client.PostAsJsonAsync($"/api/expenses/{id}/correct", new
        {
            categoryName = "Rent", amount, paidTo = "Landlord", expenseDate = date,
        }, Ct);
}
