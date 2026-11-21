using System.Net;
using System.Net.Http.Json;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408dLicensingTests
{
    private static readonly TimeSpan BeyondLockBudget = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("setup")]
    [InlineData("setup/sample")]
    public async Task SecuritySuspensionWaitsForTheSourceReadAndThenFencesItsDelivery(string route)
    {
        var (host, client, shop, binding, end) = await OpenStockedShopAsync();
        using (host)
        using (client)
        {
            await using var expiry = Open(Migrator);
            await using var hold = await expiry.Database.BeginTransactionAsync(Ct);
            await expiry.Database.ExecuteSqlAsync($"SELECT 1 FROM shop_expiry_states WHERE shop_id = {shop} FOR UPDATE", Ct);
            await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
            await delivery.Database.OpenConnectionAsync(Ct);

            var reading = client.GetAsync($"/api/shops/{shop}/{route}", Ct);
            Task? suspending = null;
            try
            {
                var reader = await WaitingBackendAsync(reading, Pid(expiry));
                suspending = ApplyAsync(binding, end, 2, shopSuspension: "Security", connection: delivery);
                await AssertWaitingAsync(suspending, reader, Pid(delivery));
            }
            finally
            {
                await hold.RollbackAsync(Ct);
                await Task.WhenAll(new Task[] { reading }.Concat(suspending is null ? [] : [suspending]));
            }

            // The read transaction acquired its source locks first. The queued projection
            // then won the separate transport boundary while the protected result was prepared.
            await AssertProblemAsync(await reading, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
            await AssertProblemAsync(await client.GetAsync($"/api/shops/{shop}/{route}", Ct),
                HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        }
    }

    [Theory]
    [InlineData("setup")]
    [InlineData("setup/sample")]
    public async Task SecuritySuspensionThatWinsRefusesTheProtectedRead(string route)
    {
        var (host, client, shop, binding, end) = await OpenStockedShopAsync();
        using (host)
        using (client)
        {
            await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "entitlement_snapshot_inbox", shop);
            await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
            await delivery.Database.OpenConnectionAsync(Ct);
            var suspending = ApplyAsync(binding, end, 2, shopSuspension: "Security", connection: delivery);
            var writer = await barrier.WaitForArrivalAsync();
            Assert.Equal(Pid(delivery), writer);

            var reading = client.GetAsync($"/api/shops/{shop}/{route}", Ct);
            try
            {
                await AssertWaitingAsync(reading, writer);
            }
            finally
            {
                await barrier.ReleaseAsync();
                await suspending;
            }

            using var refused = await reading;
            var body = await refused.Content.ReadAsStringAsync(Ct);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("Licence.SecuritySuspended", body);
            Assert.DoesNotContain("P04-08d merchant", body);
            Assert.DoesNotContain("\"document\"", body);
            Assert.DoesNotContain("\"steps\"", body);
        }
    }

    [Theory]
    [InlineData("profile", "actor")]
    [InlineData("profile", "shop")]
    [InlineData("create", "actor")]
    [InlineData("create", "shop")]
    public async Task HeldAuthorityRowsAnswerBusyAndReleaseTheFence(string operation, string held)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        await ApplyAsync(binding, _clock.Now.AddDays(1));
        await EnforceAsync(shop);
        var actor = ReadSession(client).UserId;
        Func<Task<HttpResponseMessage>> send = operation == "profile"
            ? () => client.PutAsJsonAsync($"/api/shops/{shop}", Profile("Retried name", "42 MG Road"), Ct)
            : () => client.PostAsJsonAsync("/api/shops", Profile("Second shop", "Second location"), Ct);

        await using (var blocker = Open(Migrator))
        {
            await using var hold = await blocker.Database.BeginTransactionAsync(Ct);
            if (held == "actor")
                await blocker.Database.ExecuteSqlAsync($"SELECT 1 FROM users WHERE id = {actor} FOR UPDATE", Ct);
            else
                await blocker.Database.ExecuteSqlAsync($"SELECT 1 FROM shops WHERE id = {shop} FOR UPDATE", Ct);

            await AssertProblemAsync(await send().WaitAsync(BeyondLockBudget, Ct), HttpStatusCode.Conflict, "Licence.Busy");
            await AssertFenceFreeAsync(binding);
            await AssertSetupFactsAsync(shop, actor, "P04-08d merchant", 1);
        }

        using var retried = await send().WaitAsync(BeyondLockBudget, Ct);
        await ReadSuccessAsync(retried);
        await AssertSetupFactsAsync(shop, actor, operation == "profile" ? "Retried name" : "P04-08d merchant",
            operation == "profile" ? 1 : 2);
    }

    [Theory]
    [InlineData("users")]
    [InlineData("shops")]
    public async Task BoundedAuthorityLockRestoresTheCallersTimeout(string table)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var actor = ReadSession(client).UserId;
        await using var db = Open(Migrator);
        var authority = new MembershipAuthorityRepository(db, new UnitOfWork(db));
        Task<RowLockOutcome> Lock() => table == "users"
            ? authority.TryLockUserAsync(actor, forShare: false, Ct)
            : authority.TryLockShopAsync(shop, Ct);

        await using var work = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {"7s"}, true)", Ct);
        await using (var blocker = Open(Migrator))
        {
            await using var hold = await blocker.Database.BeginTransactionAsync(Ct);
            if (table == "users")
                await blocker.Database.ExecuteSqlAsync($"SELECT 1 FROM users WHERE id = {actor} FOR UPDATE", Ct);
            else
                await blocker.Database.ExecuteSqlAsync($"SELECT 1 FROM shops WHERE id = {shop} FOR UPDATE", Ct);
            Assert.Equal(RowLockOutcome.TimedOut, await Lock());
            Assert.Equal("7s", await LockTimeoutAsync(db));
        }

        Assert.Equal(RowLockOutcome.Acquired, await Lock());
        Assert.Equal("7s", await LockTimeoutAsync(db));
    }

    private async Task<(ApiWebApplicationFactory Host, HttpClient Client, Guid Shop, Binding Binding, DateTimeOffset End)> OpenStockedShopAsync(string? email = null)
    {
        var host = CreateHost();
        await host.InitializeAsync();
        var client = CreateClient(host);
        if (email is not null) await RegisterAsync(client, email);
        var shop = await CreateShopAsync(client, register: email is null);
        using var received = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "Setup goods", barcode = $"setup-{Guid.NewGuid():N}"[..20], uom = "pcs", batchNumber = "setup-lot", quantity = 5m,
            totalPurchaseCost = 50m, mrp = 20m, salesPrice = 20m, taxRatePercent = 0m, taxIncluded = false, purchaseTaxIncluded = false,
        }, Ct);
        await ReadSuccessAsync(received);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        return (host, client, shop, binding, end);
    }

    private async Task<int> WaitingBackendAsync(Task operation, int blocker)
    {
        await using var observer = Open(Migrator);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!operation.IsCompleted)
        {
            var pid = await observer.Database.SqlQuery<int>($"""
                SELECT pid AS "Value" FROM pg_stat_activity
                WHERE wait_event_type = 'Lock' AND {blocker} = ANY(pg_blocking_pids(pid)) LIMIT 1
                """).SingleOrDefaultAsync(deadline.Token);
            if (pid != 0) return pid;
            await Task.Delay(10, deadline.Token);
        }
        Assert.Fail("The read completed before reaching its expected database wait.");
        return 0;
    }

    private async Task AssertFenceFreeAsync(Binding binding)
    {
        await using var db = Open(Migrator);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {"1s"}, true)", Ct);
        await db.Database.ExecuteSqlAsync(
            $"SELECT public.take_merchant_licence_fence({binding.Account}::uuid, {binding.Shop}, false)", Ct);
    }

    private async Task AssertSetupFactsAsync(Guid shop, Guid actor, string name, int memberships)
    {
        await using var db = Open(Migrator);
        Assert.Equal(name, await db.Shops.Where(x => x.Id == shop).Select(x => x.Name).SingleAsync(Ct));
        Assert.Equal(memberships, await db.ShopMemberships.CountAsync(x => x.UserId == actor, Ct));
    }

    private static Task<string> LockTimeoutAsync(ApplicationDbContext db) =>
        db.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync(Ct);
}
