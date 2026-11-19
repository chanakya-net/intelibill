using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Intelibill.Application.Common.Security;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408bLicensingTests
{
    [Fact]
    public async Task Customer_admitted_first_commits_before_restriction()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "customers", binding.Shop);
        await using var delivery = await OpenDeliveryAsync();

        var writing = CreateCustomerAsync(client, "first");
        var writerPid = await barrier.WaitForArrivalAsync();
        var restricting = ApplyTradingRestrictionAsync(binding, delivery);
        try
        {
            await AssertBlockedAsync(restricting, writerPid, Pid(delivery));
        }
        finally
        {
            await barrier.ReleaseAsync();
            await Task.WhenAll(writing, restricting);
        }

        using var committed = await writing;
        await ReadSuccessAsync(committed);
        await AssertProblemAsync(await CreateCustomerAsync(client, "second"), HttpStatusCode.Forbidden,
            "Licence.TradingRestricted");
        await AssertCustomerCountAsync(binding.Shop, 1);
    }

    [Fact]
    public async Task Restriction_applied_first_refuses_the_waiting_customer()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await using var holder = Open(Migrator);
        await using var pause = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlRawAsync("LOCK TABLE public.entitlement_request_uses IN ACCESS EXCLUSIVE MODE", Ct);
        await using var delivery = await OpenDeliveryAsync();
        var restricting = ApplyTradingRestrictionAsync(binding, delivery);
        Task<HttpResponseMessage>? writing = null;
        try
        {
            await AssertBlockedAsync(restricting, Pid(holder), Pid(delivery));
            writing = CreateCustomerAsync(client, "waiting");
            await AssertBlockedAsync(writing, Pid(delivery));
        }
        finally
        {
            await pause.RollbackAsync(Ct);
            await restricting;
        }

        await AssertProblemAsync(await writing!, HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertCustomerCountAsync(binding.Shop, 0);
    }

    [Fact]
    public async Task Customer_writer_revalidates_a_session_revoked_while_waiting()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var sessionId = ReadSession(client).SessionId;
        await using var holder = await OpenDeliveryAsync();
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await LockShopLicenceAsync(holder, binding.Shop);
        var writing = CreateCustomerAsync(client, "revoked");
        try
        {
            await AssertBlockedAsync(writing, Pid(holder));
            await using var db = Open(Migrator);
            await db.Database.ExecuteSqlAsync($"""
                UPDATE auth_sessions SET revoked_at = {_clock.Now} WHERE id = {sessionId}
                """, Ct);
        }
        finally
        {
            await tx.RollbackAsync(Ct);
        }

        await AssertProblemAsync(await writing, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated");
        await AssertCustomerCountAsync(binding.Shop, 0);
    }

    [Fact]
    public async Task Contended_customer_creation_returns_busy_without_writing()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await using var holder = await OpenDeliveryAsync();
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await LockShopLicenceAsync(holder, binding.Shop);
        await AssertProblemAsync(await CreateCustomerAsync(client, "busy"), HttpStatusCode.Conflict, "Licence.Busy");
        await tx.RollbackAsync(Ct);
        await AssertCustomerCountAsync(binding.Shop, 0);
        using var retry = await CreateCustomerAsync(client, "busy");
        await ReadSuccessAsync(retry);
    }

    private async Task<Binding> OpenTradingAsync(HttpClient client)
    {
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        await ApplyAsync(binding, _clock.Now.AddDays(1));
        await EnforceAsync(shop);
        return binding;
    }

    private Task ApplyTradingRestrictionAsync(Binding binding, ApplicationDbContext delivery) =>
        ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction: "Trading", connection: delivery);

    private async Task<ApplicationDbContext> OpenDeliveryAsync()
    {
        var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await db.Database.OpenConnectionAsync(Ct);
        return db;
    }

    private static Task<int> LockShopLicenceAsync(ApplicationDbContext db, Guid shop) =>
        db.Database.ExecuteSqlAsync($"SELECT public.take_merchant_licence_fence(NULL::uuid, {shop}, false)", Ct);

    private static int Pid(ApplicationDbContext db) => ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;

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
                _output.WriteLine("PostgreSQL pg_blocking_pids: backend {0} waits for backend {1}", waitingPid, blocker);
                return;
            }

            Assert.False(request.IsCompleted, "The operation must wait for the current lock holder.");
            await Task.Delay(10, deadline.Token);
        }
    }

    private async Task AssertCustomerCountAsync(Guid shop, int expected)
    {
        await using var db = Open(Migrator);
        var count = await db.Customers.CountAsync(x => x.ShopId == shop, Ct);
        Assert.Equal(expected, count);
        _output.WriteLine("PostgreSQL customer rows: {0}", count);
    }

    private static SessionClaims ReadSession(HttpClient client) => SessionClaims.TryRead(ReadPrincipal(client))!;

    private static ClaimsPrincipal ReadPrincipal(HttpClient client)
    {
        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        return new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims.Append(
            new Claim(ClaimTypes.NameIdentifier, jwt.Subject)), "P04-08b fixture"));
    }
}
