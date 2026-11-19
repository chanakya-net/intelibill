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

        using var larger = await CorrectAsync(client, original, 150m, date);
        await AssertProblemAsync(larger, HttpStatusCode.Conflict, "Expense.CorrectionSourceIneligible");
        using var moved = await CorrectAsync(client, original, 100m, date.AddDays(1));
        await AssertProblemAsync(moved, HttpStatusCode.Conflict, "Expense.CorrectionSourceIneligible");
        await using (var db = Open(Migrator))
        {
            Assert.Equal(1, await db.Expenses.CountAsync(x => x.ShopId == shop, Ct));
            Assert.False((await db.Expenses.SingleAsync(x => x.Id == original, Ct)).IsVoided);
        }

        using var eligible = await CorrectAsync(client, original, 60m, date);
        Assert.Equal(HttpStatusCode.OK, eligible.StatusCode);
        await using var after = Open(Migrator);
        Assert.Equal(2, await after.Expenses.CountAsync(x => x.ShopId == shop, Ct));
        Assert.True((await after.Expenses.SingleAsync(x => x.Id == original, Ct)).IsVoided);
        using var fresh = await CreateExpenseAsync(client);
        await AssertProblemAsync(fresh, HttpStatusCode.Forbidden, "Licence.Expired");
    }

    private static Task<HttpResponseMessage> CorrectAsync(HttpClient client, Guid id, decimal amount, DateOnly date) =>
        client.PostAsJsonAsync($"/api/expenses/{id}/correct", new
        {
            categoryName = "Rent", amount, paidTo = "Landlord", expenseDate = date,
        }, Ct);
}
