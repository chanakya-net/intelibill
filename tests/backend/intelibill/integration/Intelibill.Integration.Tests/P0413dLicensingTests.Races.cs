using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413dLicensingTests
{
    [Fact]
    public async Task Scheduled_security_is_rejudged_after_the_supplier_wait()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        var end = _clock.Now.AddMinutes(1);
        var effective = end.AddSeconds(1);
        await ApplyAsync(source.Binding, end, securityEffectiveAt: effective);
        await EnforceAsync(source.Shop);
        _clock.Now = end;
        var version = await ReceiptVersionAsync(source.Receipt);
        var before = await FactsAsync(source);

        await using var db = Open(Migrator);
        await using var held = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync(
            $"SELECT id FROM suppliers WHERE id = {source.Supplier} FOR NO KEY UPDATE", Ct);
        var blocker = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
        var request = CorrectAsync(client, source.Receipt, version, 1100, NewKey());
        int waiter;
        try
        {
            waiter = await WaitForBlockedRequestAsync(request, blocker);
            _clock.Now = effective;
        }
        finally
        {
            await held.RollbackAsync(Ct);
        }

        Assert.NotEqual(0, waiter);
        await AssertProblemAsync(await request, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        Assert.Equal(before, await FactsAsync(source));
        _output.WriteLine("Supplier locker PID {0}, waiter {1}; security took effect at {2:O}; no writes.", blocker, waiter, effective);

        await ApplyAsync(source.Binding, end, 2, state: "Expired", verified: end.AddSeconds(-1), shopReleasedRevision: 1);
        await AssertProblemAsync(await client.GetAsync($"/api/licensing/shops/{source.Shop}/trading-authority", Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
        using var success = await CorrectAsync(client, source.Receipt, version, 1100, NewKey());
        await ReadSuccessAsync(success);
    }

    [Fact]
    public async Task Posting_winner_finishes_before_account_security_and_an_unrelated_shop_stays_available()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var unrelated = CreateClient(host);
        var source = await SeedAsync(client);
        var other = await SeedAsync(unrelated);
        await ExpireAsync(client, source);
        await ExpireAsync(unrelated, other);
        var version = await ReceiptVersionAsync(source.Receipt);

        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "purchase_receipt_cost_adjustments", source.Shop);
        var correction = CorrectAsync(client, source.Receipt, version, 1100, NewKey());
        var blocker = await barrier.WaitForArrivalAsync();
        await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var projection = ApplyAsync(source.Binding, _clock.Now.AddDays(1), 2, restriction: "Security", connection: delivery);
        var waiter = await ObserveWaitAsync(projection, blocker, barrier, projection);
        Assert.NotEqual(0, waiter);
        await AssertProblemAsync(await correction, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        var committed = await FactsAsync(source);
        Assert.Equal(1100m, committed.Effective);
        Assert.Equal(1, committed.Adjustments);

        await AssertProblemAsync(await CorrectAsync(client, source.Receipt, await ReceiptVersionAsync(source.Receipt), 1050, NewKey()),
            HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        using var allowed = await CorrectAsync(unrelated, other.Receipt, await ReceiptVersionAsync(other.Receipt), 1010, NewKey());
        Assert.Equal(10m, (await ReadSuccessAsync(allowed)).GetProperty("delta").GetDecimal());
        _output.WriteLine("Account projection waiter PID {0}, posting blocker PID {1}. The posting committed, its result was withheld, and the unrelated shop completed.", waiter, blocker);
    }

    private async Task<int> WaitForBlockedRequestAsync(Task request, int blocker)
    {
        await using var observer = Open(Migrator);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!request.IsCompleted)
        {
            var waiter = await observer.Database.SqlQuery<int>($"""
                SELECT pid AS "Value" FROM pg_stat_activity
                WHERE wait_event_type = 'Lock' AND {blocker} = ANY(pg_blocking_pids(pid)) LIMIT 1
                """).SingleOrDefaultAsync(deadline.Token);
            if (waiter != 0) return waiter;
            await Task.Delay(10, deadline.Token);
        }

        return 0;
    }

    [Fact]
    public async Task Concurrent_different_keys_against_one_receipt_version_commit_one_delta()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var source = await SeedAsync(client);
        await ExpireAsync(client, source);
        var version = await ReceiptVersionAsync(source.Receipt);
        var before = await FactsAsync(source);
        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "purchase_receipt_cost_adjustments", source.Shop);
        var first = CorrectAsync(client, source.Receipt, version, 1100, NewKey());
        Task<HttpResponseMessage>? second = null;
        try
        {
            var blocker = await barrier.WaitForArrivalAsync();
            second = CorrectAsync(client, source.Receipt, version, 1050, NewKey());
            var waiter = await barrier.WaitForBlockedByAsync(blocker);
            _output.WriteLine("Second correction PID {0} waited on posting PID {1}.", waiter, blocker);
        }
        finally
        {
            await barrier.ReleaseAsync();
        }

        using var winner = await first;
        Assert.Equal(100m, (await ReadSuccessAsync(winner)).GetProperty("delta").GetDecimal());
        await AssertProblemAsync(await second!, HttpStatusCode.Conflict, "PurchaseReceipt.VersionConflict");
        Assert.Equal(before with { Effective = 1100m, Adjustments = 1, Ledger = before.Ledger + 1, Operations = 1 },
            await FactsAsync(source));
    }
}
