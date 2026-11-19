using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Intelibill.Application.Common.Security;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408cLicensingTests
{
    [Fact]
    public async Task Supplier_admitted_first_commits_before_restriction()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "suppliers", binding.Shop);
        await using var delivery = await OpenDeliveryAsync();

        var writing = CreateSupplierAsync(client, "first");
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
        await AssertProblemAsync(await CreateSupplierAsync(client, "second"), HttpStatusCode.Forbidden,
            "Licence.TradingRestricted");
        await AssertSupplierCountAsync(binding.Shop, 1);
    }

    [Fact]
    public async Task Restriction_applied_first_refuses_the_waiting_supplier()
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
            writing = CreateSupplierAsync(client, "waiting");
            await AssertBlockedAsync(writing, Pid(delivery));
        }
        finally
        {
            await pause.RollbackAsync(Ct);
            await restricting;
        }

        await AssertProblemAsync(await writing!, HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertSupplierCountAsync(binding.Shop, 0);
    }

    [Fact]
    public async Task Supplier_writer_revalidates_a_session_revoked_while_waiting()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var sessionId = ReadSession(client).SessionId;
        await using var holder = await OpenDeliveryAsync();
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await LockShopLicenceAsync(holder, binding.Shop);
        var writing = CreateSupplierAsync(client, "revoked");
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
        await AssertSupplierCountAsync(binding.Shop, 0);
    }

    [Fact]
    public async Task Contended_supplier_creation_returns_busy_without_writing()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await using var holder = await OpenDeliveryAsync();
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await LockShopLicenceAsync(holder, binding.Shop);
        await AssertProblemAsync(await CreateSupplierAsync(client, "busy"), HttpStatusCode.Conflict, "Licence.Busy");
        await tx.RollbackAsync(Ct);
        await AssertSupplierCountAsync(binding.Shop, 0);
        using var retry = await CreateSupplierAsync(client, "busy");
        await ReadSuccessAsync(retry);
    }

    [Fact]
    public async Task Supplier_writer_waiting_for_fence_uses_current_time()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        await using var holder = await OpenDeliveryAsync();
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await LockShopLicenceAsync(holder, shop);
        var writing = CreateSupplierAsync(client, "Crossed expiry");
        try
        {
            await AssertBlockedAsync(writing, Pid(holder));
            _clock.Now = end;
        }
        finally
        {
            await tx.RollbackAsync(Ct);
        }

        await AssertProblemAsync(await writing, HttpStatusCode.Forbidden, "Licence.Expired");
        await AssertSupplierCountAsync(shop, 0);
    }

    [Fact]
    public async Task Restricted_edit_cannot_undo_a_concurrent_deactivation()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        using var created = await CreateSupplierAsync(client, "Active supplier");
        var supplierId = (await ReadSuccessAsync(created)).GetProperty("supplierId").GetGuid();
        _clock.Now = _clock.Now.AddDays(1);
        await using var holder = Open(Migrator);
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync($"UPDATE suppliers SET is_active = false WHERE id = {supplierId}", Ct);
        var writing = client.PutAsJsonAsync($"/api/suppliers/{supplierId}", SupplierInput("Stale edit", true), Ct);
        try
        {
            await AssertBlockedAsync(writing, Pid(holder));
        }
        finally
        {
            await tx.CommitAsync(Ct);
        }

        await AssertProblemAsync(await writing, HttpStatusCode.Forbidden, "Licence.Expired");
        await using var db = Open(Migrator);
        var supplier = await db.Suppliers.SingleAsync(x => x.Id == supplierId && x.ShopId == binding.Shop, Ct);
        Assert.False(supplier.IsActive);
        Assert.Equal("Active supplier", supplier.Name);
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

    private async Task AssertSupplierCountAsync(Guid shop, int expected)
    {
        var names = await SupplierNamesAsync(shop);
        Assert.Equal(expected, names.Length);
        _output.WriteLine("PostgreSQL ordinary supplier rows: {0}", names.Length);
    }

    private static SessionClaims ReadSession(HttpClient client) => SessionClaims.TryRead(ReadPrincipal(client))!;

    private static ClaimsPrincipal ReadPrincipal(HttpClient client)
    {
        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        return new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims.Append(
            new Claim(ClaimTypes.NameIdentifier, jwt.Subject)), "P04-08c fixture"));
    }
}
