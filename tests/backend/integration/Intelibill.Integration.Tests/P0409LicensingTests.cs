using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0409LicensingTests
{
    [Fact]
    public async Task Expense_creation_is_refused_at_expiry_without_persisting_a_financial_fact()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        _clock.Now = end;
        using var response = await client.PostAsJsonAsync("/api/expenses", new
        {
            categoryName = "Rent", amount = 100m, paidTo = "Landlord",
            expenseDate = DateOnly.FromDateTime(_clock.Now.UtcDateTime),
        }, Ct);
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Licence.Expired");
        await using var db = Open(Migrator);
        Assert.Equal(0, await db.Expenses.CountAsync(x => x.ShopId == shop, Ct));
    }
    [Fact]
    public async Task Expense_mutation_waits_on_licence_fence_and_is_isolated_by_shop()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var first = CreateClient(host);
        using var second = CreateClient(host);
        var shop = await CreateShopAsync(first);
        var other = await CreateShopAsync(second);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1));
        await EnforceAsync(shop);
        await ApplyAsync(await BindAsync(other), _clock.Now.AddDays(1));
        await EnforceAsync(other);
        await using var holder = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync($"SELECT public.take_merchant_licence_fence(NULL::uuid, {shop}, false)", Ct);
        using var isolated = await CreateExpenseAsync(second);
        Assert.Equal(HttpStatusCode.Created, isolated.StatusCode);
        using var blocked = await CreateExpenseAsync(first);
        await AssertProblemAsync(blocked, HttpStatusCode.Conflict, "Licence.Busy");
        await tx.RollbackAsync(Ct);
        await using var db = Open(Migrator);
        Assert.Equal(0, await db.Expenses.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(1, await db.Expenses.CountAsync(x => x.ShopId == other, Ct));
    }

    private Task<HttpResponseMessage> CreateExpenseAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/expenses", new
        {
            categoryName = "Rent", amount = 100m, paidTo = "Landlord",
            expenseDate = DateOnly.FromDateTime(_clock.Now.UtcDateTime),
        }, Ct);
}
