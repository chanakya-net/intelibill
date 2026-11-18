using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0405LicensingTests
{
    [Theory]
    [InlineData("Trading", "Active", "Licence.TradingRestricted")]
    [InlineData("Unrestricted", "Expired", "Licence.Expired")]
    public async Task Sale_admitted_first_commits_before_restriction(string restriction, string state, string code)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var service = await SeedServiceAsync(client);
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "sales", binding.Shop);
        await using var delivery = OpenDelivery();
        await delivery.Database.OpenConnectionAsync(Ct);
        var deliveryPid = ((NpgsqlConnection)delivery.Database.GetDbConnection()).ProcessID;

        var sale = PostSaleAsync(client, SaleRequest(service));
        var salePid = await barrier.WaitForArrivalAsync();
        var restricting = ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction, state, connection: delivery);
        try
        {
            await AssertBlockedAsync(restricting, salePid, deliveryPid);
        }
        finally
        {
            await barrier.ReleaseAsync();
            await Task.WhenAll(sale, restricting);
        }

        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", SaleRequest(service), Ct),
            HttpStatusCode.Forbidden, code);
        await AssertSaleCountAsync(binding.Shop, 1);
    }

    [Theory]
    [InlineData("Trading", "Active", "Licence.TradingRestricted")]
    [InlineData("Unrestricted", "Expired", "Licence.Expired")]
    public async Task Restriction_applied_first_refuses_the_waiting_sale(string restriction, string state, string code)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var payload = SaleRequest(await SeedServiceAsync(client));
        await using var holder = Open(Migrator);
        await using var pause = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlRawAsync("LOCK TABLE public.entitlement_request_uses IN ACCESS EXCLUSIVE MODE", Ct);
        var holderPid = ((NpgsqlConnection)holder.Database.GetDbConnection()).ProcessID;
        await using var delivery = OpenDelivery();
        await delivery.Database.OpenConnectionAsync(Ct);
        var deliveryPid = ((NpgsqlConnection)delivery.Database.GetDbConnection()).ProcessID;
        var restricting = ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction, state, connection: delivery);
        Task<HttpResponseMessage>? sale = null;
        try
        {
            await AssertBlockedAsync(restricting, holderPid, deliveryPid);
            sale = client.PostAsJsonAsync("/api/sales", payload, Ct);
            await AssertBlockedAsync(sale, deliveryPid);
        }
        finally
        {
            await pause.RollbackAsync(Ct);
            await restricting;
        }

        await AssertProblemAsync(await sale!, HttpStatusCode.Forbidden, code);
        await AssertNoPostingAsync(binding.Shop);
    }

    [Fact]
    public async Task Retry_waits_for_its_in_flight_attempt_at_expiry_and_replays()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var end = _clock.Now.AddMinutes(1);
        var binding = await OpenTradingAsync(client, end);
        var service = await SeedServiceAsync(client);
        var request = SaleRequest(service);
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "sales", binding.Shop);

        var first = PostSaleAsync(client, request);
        var firstPid = await barrier.WaitForArrivalAsync();
        _clock.Now = end;
        var retry = PostSaleAsync(client, request);
        try
        {
            await AssertBlockedAsync(retry, firstPid);
        }
        finally
        {
            await barrier.ReleaseAsync();
        }

        var original = await first;
        AssertReplay(original, await retry);
        Assert.True(original.GetProperty("soldAt").GetDateTimeOffset() < end);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales", SaleRequest(service), Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
        await AssertSaleCountAsync(binding.Shop, 1);
    }

    private async Task<Binding> OpenTradingAsync(HttpClient client, DateTimeOffset? end = null)
    {
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        await ApplyAsync(binding, end ?? _clock.Now.AddDays(1));
        await EnforceAsync(shop);
        return binding;
    }

    private ApplicationDbContext OpenDelivery() => Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));

    private async Task AssertSaleCountAsync(Guid shop, int expected)
    {
        await using var db = Open(Migrator);
        Assert.Equal(expected, await db.Sales.CountAsync(x => x.ShopId == shop, Ct));
    }

    private async Task AssertBlockedAsync(Task request, int blocker, int? waiter = null)
    {
        await using var observer = Open(Migrator);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (true)
        {
            var waitingPid = await observer.Database.SqlQuery<int>($"""
                SELECT pid AS "Value" FROM pg_stat_activity
                WHERE wait_event_type = 'Lock' AND ({waiter}::int IS NULL OR pid = {waiter})
                    AND {blocker} = ANY(pg_blocking_pids(pid))
                LIMIT 1
                """).SingleOrDefaultAsync(deadline.Token);
            if (waitingPid != 0)
            {
                _output.WriteLine($"PostgreSQL pg_blocking_pids: backend {waitingPid} waits for backend {blocker}.");
                return;
            }

            Assert.False(request.IsCompleted, "The operation must wait for the current lock holder.");
            await Task.Delay(10, deadline.Token);
        }
    }

    private static void AssertReplay(JsonElement original, JsonElement replay)
    {
        Assert.Equal(original.GetProperty("saleId").GetGuid(), replay.GetProperty("saleId").GetGuid());
        Assert.Equal(original.GetProperty("invoiceNumber").GetString(), replay.GetProperty("invoiceNumber").GetString());
    }
}
