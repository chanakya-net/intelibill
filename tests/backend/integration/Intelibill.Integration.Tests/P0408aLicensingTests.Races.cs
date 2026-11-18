using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Items.Commands.AddItem;
using Intelibill.Application.Features.Items.Queries.GetProductDetails;
using Intelibill.Application.Features.Services.Commands.AddService;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408aLicensingTests
{
    [Theory]
    [InlineData("item", "items")]
    [InlineData("service", "services")]
    [InlineData("import", "items")]
    public async Task Definition_admitted_first_commits_before_restriction(string writer, string table)
    {
        using var host = CreateHost(new Catalogue());
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, table, binding.Shop);
        await using var delivery = await OpenDeliveryAsync();

        var writing = WriteAsync(client, writer, "first");
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
        await AssertProblemAsync(await WriteAsync(client, writer, "second"), HttpStatusCode.Forbidden,
            "Licence.TradingRestricted");
        await AssertDefinitionCountAsync(binding.Shop, writer, 1);
    }

    [Theory]
    [InlineData("item")]
    [InlineData("service")]
    [InlineData("import")]
    public async Task Restriction_applied_first_refuses_the_waiting_definition(string writer)
    {
        using var host = CreateHost(new Catalogue());
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
            writing = WriteAsync(client, writer, "waiting");
            await AssertBlockedAsync(writing, Pid(delivery));
        }
        finally
        {
            await pause.RollbackAsync(Ct);
            await restricting;
        }

        await AssertProblemAsync(await writing!, HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertDefinitionCountAsync(binding.Shop, writer, 0);
    }

    [Fact]
    public async Task Concurrent_catalogue_imports_return_the_one_committed_definition()
    {
        using var host = CreateHost(new Catalogue());
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "items", binding.Shop);
        var first = WriteAsync(client, "import", "shared-barcode");
        var writerPid = await barrier.WaitForArrivalAsync();
        var second = WriteAsync(client, "import", "shared-barcode");
        try
        {
            await AssertBlockedAsync(second, writerPid);
        }
        finally
        {
            await barrier.ReleaseAsync();
        }
        using var original = await first;
        using var winner = await second;
        Assert.Equal((await ReadSuccessAsync(original)).GetRawText(), (await ReadSuccessAsync(winner)).GetRawText());
        await AssertDefinitionCountAsync(binding.Shop, "import", 1);
    }

    [Fact]
    public async Task Definition_writer_revalidates_a_session_revoked_while_waiting()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var sessionId = ReadSession(client).SessionId;
        await using var holder = await OpenDeliveryAsync();
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await LockShopLicenceAsync(holder, binding.Shop);
        var writing = WriteAsync(client, "item", "revoked");
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
        await AssertDefinitionCountAsync(binding.Shop, "item", 0);
    }

    [Fact]
    public async Task Contended_definition_returns_busy_without_writing()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await using var holder = await OpenDeliveryAsync();
        await using var tx = await holder.Database.BeginTransactionAsync(Ct);
        await LockShopLicenceAsync(holder, binding.Shop);
        await AssertProblemAsync(await WriteAsync(client, "item", "busy"), HttpStatusCode.Conflict, "Licence.Busy");
        await tx.RollbackAsync(Ct);
        await AssertDefinitionCountAsync(binding.Shop, "item", 0);
        using var retry = await WriteAsync(client, "item", "busy");
        await ReadSuccessAsync(retry);
    }

    [Theory]
    [InlineData("Trading", false, "Licence.TradingRestricted")]
    [InlineData("Trading", true, null)]
    [InlineData("Security", true, "Licence.SecuritySuspended")]
    public async Task Catalogue_fetch_leaves_projection_free_and_rechecks_local_history(
        string restriction, bool createWhileWaiting, string? refusal)
    {
        var arrival = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = CreateHost(new Catalogue(_ => { arrival.TrySetResult(); return release.Task; }));
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        var lookup = client.GetAsync(Details("catalogue-race"), Ct);
        try
        {
            await arrival.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (createWhileWaiting)
            {
                using var created = await WriteAsync(client, "item", "catalogue-race");
                await ReadSuccessAsync(created);
            }
            await ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult();
        }

        using var response = await lookup;
        if (refusal is null)
            Assert.Equal("catalogue-race", (await ReadSuccessAsync(response)).GetProperty("name").GetString());
        else
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, refusal);
        await AssertDefinitionCountAsync(binding.Shop, "import", createWhileWaiting ? 1 : 0);
    }

    [Fact]
    public async Task Direct_handlers_cannot_bypass_the_source_fence()
    {
        using var host = CreateHost(new Catalogue());
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenTradingAsync(client);
        await ApplyAsync(binding, _clock.Now.AddDays(1), 2, restriction: "Trading");
        var session = ReadSession(client);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = ReadPrincipal(client) };
        try
        {
            var item = ActivatorUtilities.CreateInstance<AddItemCommandHandler>(services);
            var service = ActivatorUtilities.CreateInstance<AddServiceCommandHandler>(services);
            var import = ActivatorUtilities.CreateInstance<GetProductDetailsByNameOrBarcodeQueryHandler>(services);
            Assert.Equal("Licence.TradingRestricted",
                (await item.HandleAsync(new(session, "Item", "direct", null, "pcs", true, null, 0), Ct)).FirstError.Code);
            Assert.Equal("Licence.TradingRestricted",
                (await service.HandleAsync(new(session, "Repair", null, 10m, null, 0, false, true), Ct)).FirstError.Code);
            Assert.Equal("Licence.TradingRestricted",
                (await import.HandleAsync(new(session, null, "direct-import", null), Ct)).FirstError.Code);
        }
        finally
        {
            accessor.HttpContext = null;
        }
        await AssertDefinitionCountAsync(binding.Shop, "item", 0);
        await AssertDefinitionCountAsync(binding.Shop, "service", 0);
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

    private async Task AssertDefinitionCountAsync(Guid shop, string writer, int expected)
    {
        await using var db = Open(Migrator);
        var count = writer == "service"
            ? await db.Services.CountAsync(x => x.ShopId == shop, Ct)
            : await db.Items.CountAsync(x => x.ShopId == shop, Ct);
        Assert.Equal(expected, count);
        _output.WriteLine("PostgreSQL {0} definition count: {1}", writer, count);
    }

    private static Task<HttpResponseMessage> WriteAsync(HttpClient client, string writer, string key) => writer switch
    {
        "item" => client.PostAsJsonAsync("/api/items", ItemRequest(key), Ct),
        "service" => client.PostAsJsonAsync("/api/services", ServiceRequest(key), Ct),
        "import" => client.GetAsync(Details(key), Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(writer)),
    };

    private static SessionClaims ReadSession(HttpClient client) => SessionClaims.TryRead(ReadPrincipal(client))!;

    private static ClaimsPrincipal ReadPrincipal(HttpClient client)
    {
        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        return new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims.Append(
            new Claim(ClaimTypes.NameIdentifier, jwt.Subject)), "P04-08a fixture"));
    }
}
