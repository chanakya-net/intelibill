using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0409LicensingTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("unrelated")]
    [InlineData("eligible")]
    public async Task Licence_is_reevaluated_after_waiting_for_the_posting_period(string action)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        using var created = await CreateExpenseAsync(client);
        var id = (await ReadSuccessAsync(created)).GetProperty("id").GetGuid();
        var date = DateOnly.FromDateTime(_clock.Now.UtcDateTime);
        await using var holder = Open(Migrator);
        await holder.Database.OpenConnectionAsync(Ct);
        await using var transaction = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync($"SELECT id FROM posting_periods WHERE shop_id = {shop} FOR UPDATE", Ct);
        var blocker = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
        var request = action == "create" ? CreateExpenseAsync(client) : client.PostAsJsonAsync($"/api/expenses/{id}/correct", new
        {
            categoryName = "Rent", amount = 60m, paidTo = action == "eligible" ? "Landlord" : "Other", expenseDate = date,
            operationKey = Guid.NewGuid().ToString(),
        }, Ct);
        await WaitForBusinessLockAsync(request, blocker);
        _clock.Now = end;
        await transaction.RollbackAsync(Ct);
        using var response = await request;
        if (action == "create") await AssertProblemAsync(response, HttpStatusCode.Forbidden, "Licence.Expired");
        else if (action == "unrelated") await AssertProblemAsync(response, HttpStatusCode.Conflict, "Expense.CorrectionSourceIneligible");
        else Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Open(Migrator);
        Assert.Equal(action == "eligible" ? 2 : 1, await db.Expenses.CountAsync(x => x.ShopId == shop, Ct));
        Assert.Equal(action == "eligible", (await db.Expenses.SingleAsync(x => x.Id == id, Ct)).IsVoided);
        Assert.Equal(action == "eligible" ? 1 : 0, await db.OperationReceipts.CountAsync(x => x.ShopId == shop, Ct));
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
            Assert.False(request.IsCompleted, "Request completed before reaching the business lock.");
            await Task.Delay(10, deadline.Token);
        }
    }
}
