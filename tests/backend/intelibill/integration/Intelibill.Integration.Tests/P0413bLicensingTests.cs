using System.Net;
using System.Net.Http.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0413bLicensingTests
{
    [Fact]
    public async Task Restriction_winning_the_account_fence_prevents_cancellation()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var siblingClient = CreateClient(host);
        using var unrelatedClient = CreateClient(host);
        var receipt = await SeedReceiptAsync(host, client);
        var unrelated = await SeedReceiptAsync(host, unrelatedClient);
        var unrelatedBinding = await BindAsync(unrelated.Shop);
        await ApplyAsync(unrelatedBinding, Deadline);
        await EnforceAsync(unrelated.Shop);
        var sibling = await BindAsync(await CreateShopAsync(siblingClient));
        var binding = await BindAsync(receipt.Shop, sibling.Account);
        await ApplyAsync(sibling, Deadline);
        await ApplyAsync(binding, Deadline);
        await EnforceAsync(receipt.Shop);

        await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await delivery.Database.OpenConnectionAsync(Ct);
        await using var holder = Open(Migrator);
        await using var pending = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlRawAsync("LOCK TABLE public.entitlement_request_uses IN ACCESS EXCLUSIVE MODE", Ct);
        var deliveryPid = await PidAsync(delivery);
        var holderPid = await PidAsync(holder);
        var projection = ApplyAsync(sibling, Deadline, 2, "Security", connection: delivery);
        Task<HttpResponseMessage>? cancellation = null;
        int? sourcePid = null;
        try
        {
            Assert.Equal(deliveryPid, await WaitForBlockedPidAsync(holderPid, projection));
            cancellation = client.PostAsJsonAsync(receipt.Path + "/cancellations", receipt.Command(), Ct);
            sourcePid = await WaitForBlockedPidAsync(deliveryPid, cancellation);
            _output.WriteLine($"PostgreSQL holder PID {holderPid}; projection PID {deliveryPid}; cancellation PID {sourcePid}");
        }
        finally
        {
            await pending.RollbackAsync(Ct);
            await projection;
        }

        using var result = await cancellation!;
        Assert.True(sourcePid is not null,
            $"The cancellation must wait behind the account fence; it completed with {(int)result.StatusCode} while security was uncommitted.");
        await AssertProblemAsync(result, HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
        await AssertUnchangedAsync(receipt);
        using var other = await unrelatedClient.PostAsJsonAsync(unrelated.Path + "/cancellations", unrelated.Command(), Ct);
        await ReadSuccessAsync(other);
        await AssertPostedExactlyOnceAsync(unrelated);
        _output.WriteLine("PostgreSQL: forbidden sibling cancellation rolled back; unrelated account posted independently.");
    }

    private async Task<int?> WaitForBlockedPidAsync(int blocker, Task operation)
    {
        await using var observer = Open(Migrator);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!deadline.IsCancellationRequested)
        {
            var waiter = await observer.Database.SqlQuery<int?>($"""
                SELECT (SELECT pid FROM pg_stat_activity WHERE {blocker} = ANY(pg_blocking_pids(pid)) LIMIT 1) AS "Value"
                """).SingleAsync(Ct);
            if (waiter is not null || operation.IsCompleted) return waiter;
            await Task.Delay(20, Ct);
        }
        return null;
    }

    private static Task<int> PidAsync(Intelibill.Infrastructure.Data.ApplicationDbContext db) =>
        db.Database.SqlQuery<int>($"""SELECT pg_backend_pid() AS "Value" """).SingleAsync(Ct);

    private async Task AssertUnchangedAsync(Receipt receipt)
    {
        await using var db = Open(Migrator);
        Assert.Empty(await db.Set<SaleCancellation>().Where(x => x.SaleId == receipt.Sale.Id).ToListAsync(Ct));
        Assert.Empty(await db.Set<OperationReceipt>().Where(x => x.ShopId == receipt.Shop &&
            x.OperationType == FinancialOperationType.SaleCancellation).ToListAsync(Ct));
        Assert.Equal(receipt.Sale.Status, (await db.Sales.SingleAsync(x => x.Id == receipt.Sale.Id, Ct)).Status);
        Assert.Empty(await db.StockTransactions.Where(x => x.ShopId == receipt.Shop &&
            x.TransactionType == StockTransactionType.SaleCancellation).ToListAsync(Ct));
        Assert.Empty(await db.CustomerLedgerEntries.Where(x => x.ShopId == receipt.Shop &&
            x.EntryType == CustomerLedgerEntryType.SaleCancellationCredit).ToListAsync(Ct));
        Assert.Empty(await db.TillSourceRecords.Where(x => x.ShopId == receipt.Shop).ToListAsync(Ct));
        Assert.Equal(8m, (await db.InventoryBatches.SingleAsync(x => x.Id == receipt.Batch, Ct)).Quantity);
    }
}
