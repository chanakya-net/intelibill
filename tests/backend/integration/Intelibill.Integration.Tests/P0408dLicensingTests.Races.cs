using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408dLicensingTests
{
    [Fact]
    public async Task SettingsWriteUsesCurrentScopedAuthorityAcrossWaits()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(await BindAsync(shop), end);
        await EnforceAsync(shop);

        await using var blocker = Open(Migrator);
        await using var hold = await blocker.Database.BeginTransactionAsync(Ct);
        await blocker.Database.ExecuteSqlAsync($"UPDATE shops SET address = {"New registered lane"} WHERE id = {shop}", Ct);
        var writing = client.PutAsJsonAsync($"/api/shops/{shop}", Profile("Stale name", "42 MG Road"), Ct);
        try
        {
            await AssertWaitingAsync(writing, Pid(blocker));
            _clock.Now = end;
        }
        finally
        {
            await hold.CommitAsync(Ct);
        }

        await AssertProblemAsync(await writing, HttpStatusCode.Forbidden, "Licence.Expired");
        await using var db = Open(Migrator);
        var address = await db.Shops.Where(x => x.Id == shop).Select(x => x.Address).SingleAsync(Ct);
        Assert.Equal("New registered lane", address);
        using var retry = await client.PutAsJsonAsync($"/api/shops/{shop}", Profile("Only the name", address), Ct);
        Assert.Equal("Only the name", (await ReadSuccessAsync(retry)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task BankWriterAndTradingRestrictionUseTheSameFence()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);

        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "bank_accounts", shop);
        await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await delivery.Database.OpenConnectionAsync(Ct);
        var path = $"/api/shops/{shop}/bank-details";
        var writing = client.PutAsJsonAsync(path, Bank("100000000001"), Ct);
        var writerPid = await barrier.WaitForArrivalAsync();
        var restricting = ApplyAsync(binding, end, 2, restriction: "Trading", connection: delivery);
        try
        {
            await AssertWaitingAsync(restricting, writerPid, Pid(delivery));
        }
        finally
        {
            await barrier.ReleaseAsync();
            await Task.WhenAll(writing, restricting);
        }

        using var first = await writing;
        await ReadSuccessAsync(first);
        await AssertProblemAsync(await client.PutAsJsonAsync(path, Bank("100000000002"), Ct),
            HttpStatusCode.Forbidden, "Licence.TradingRestricted");

        await using var db = Open(Migrator);
        var account = await db.BankAccounts.SingleAsync(x => x.ShopId == shop, Ct);
        Assert.Equal("100000000001", account.AccountNumber);
        var generic = $"/api/bank-accounts/{account.Id}";
        await AssertProblemAsync(await client.PutAsJsonAsync(generic, Bank("100000000003"), Ct),
            HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertProblemAsync(await client.DeleteAsync(generic, Ct),
            HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        Assert.Equal("100000000001", await db.BankAccounts.Where(x => x.ShopId == shop).Select(x => x.AccountNumber).SingleAsync(Ct));
    }

    [Fact]
    public async Task FirstShopBootstrapSerializesTwoShoplessDevices()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var a = CreateClient(host);
        using var b = CreateClient(host);
        var email = $"p0408d-race-{Guid.NewGuid():N}@fixture.test";
        await RegisterAsync(a, email);
        using var login = await b.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = "Pass123!Aa" }, Ct);
        b.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(login)).GetProperty("accessToken").GetString());

        var results = await Task.WhenAll(a.PostAsJsonAsync("/api/shops", Profile("A", "First location"), Ct),
            b.PostAsJsonAsync("/api/shops", Profile("B", "Second location"), Ct));
        using var winner = Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        await AssertProblemAsync(Assert.Single(results, x => x != winner), HttpStatusCode.Conflict, "Shop.SelectExistingShop");

        var actor = Guid.Parse(new JwtSecurityToken(a.DefaultRequestHeaders.Authorization!.Parameter).Subject);
        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.UserId == actor, Ct);
        Assert.True(membership.IsDefault);
        Assert.True(await db.Suppliers.AnyAsync(x => x.ShopId == membership.ShopId && x.IsSystem, Ct));
    }

    private static int Pid(ApplicationDbContext db) => ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;

    private async Task AssertWaitingAsync(Task operation, int blocker, int? waiter = null)
    {
        await using var observer = Open(Migrator);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!operation.IsCompleted)
        {
            var pid = await observer.Database.SqlQuery<int>($"""
                SELECT pid AS "Value" FROM pg_stat_activity
                WHERE wait_event_type = 'Lock' AND ({waiter}::int IS NULL OR pid = {waiter})
                    AND {blocker} = ANY(pg_blocking_pids(pid))
                LIMIT 1
                """).SingleOrDefaultAsync(deadline.Token);
            if (pid != 0)
            {
                _output.WriteLine("PostgreSQL observed backend {0} waiting for {1}.", pid, blocker);
                return;
            }
            await Task.Delay(10, deadline.Token);
        }
        Assert.Fail("The operation completed before its expected database wait.");
    }

    private static object Bank(string accountNumber) => new
    {
        bankName = "Fixture Bank", accountNumber, accountType = "Savings",
        ifscCode = "HDFC0001234", accountHolderName = "Fixture Owner",
    };
}
