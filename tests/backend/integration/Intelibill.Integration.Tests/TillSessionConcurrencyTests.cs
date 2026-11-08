using Intelibill.Application.Features.Tills.Commands.CloseTill;
using Intelibill.Application.Features.Tills.Commands.OpenTill;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Integration.Tests.TillTestContext;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class TillSessionConcurrencyTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public async Task OpenRace_OneOpenSession()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var setup = new TillTestContext(fixture.MigratorConnectionString, shop);
        var till = await setup.CreateAsync(shop, owner);
        await using var barrier = await TillPostgresBarrier.CreateAsync(fixture.MigratorConnectionString, "till_sessions", shop);
        async Task<ErrorOr.ErrorOr<Intelibill.Application.Features.Tills.TillSessionDto>> OpenAsync()
        {
            await using var worker = new TillTestContext(fixture.MigratorConnectionString, shop);
            return await worker.Lifecycle.OpenAsync(new OpenTillCommand(shop, cashier, till.Id, cashier, 1000m, Guid.NewGuid().ToString()), default);
        }
        var one = OpenAsync();
        var firstPid = await barrier.WaitForArrivalAsync();
        var two = OpenAsync();
        await barrier.WaitForBlockedByAsync(firstPid);
        Assert.False(one.IsCompleted); Assert.False(two.IsCompleted);
        await barrier.ReleaseAsync();
        var results = await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(results, x => !x.IsError);
        Assert.Equal("Till.AlreadyOpen", Assert.Single(results, x => x.IsError).FirstError.Code);
        Assert.Equal(1, await setup.Db.TillSessions.CountAsync(x => x.ShopId == shop && x.State == TillSessionState.Open));
    }

    [Fact]
    public async Task DoubleClose_OneSnapshot()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var setup = new TillTestContext(fixture.MigratorConnectionString, shop);
        var session = await setup.OpenAsync(shop, cashier, (await setup.CreateAsync(shop, owner)).Id);
        await using var barrier = await TillPostgresBarrier.CreateAsync(fixture.MigratorConnectionString, "till_closes", shop);
        async Task<ErrorOr.ErrorOr<Intelibill.Application.Features.Tills.TillSessionDto>> CloseAsync()
        {
            await using var worker = new TillTestContext(fixture.MigratorConnectionString, shop);
            return await worker.Lifecycle.CloseAsync(new CloseTillCommand(shop, cashier, session.Id, 1000m, null, session.Version, Guid.NewGuid().ToString()), default);
        }
        var one = CloseAsync();
        var firstPid = await barrier.WaitForArrivalAsync();
        var two = CloseAsync();
        await barrier.WaitForBlockedByAsync(firstPid);
        Assert.False(one.IsCompleted); Assert.False(two.IsCompleted);
        await barrier.ReleaseAsync();
        var results = await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(results, x => !x.IsError);
        Assert.Equal("Till.VersionConflict", Assert.Single(results, x => x.IsError).FirstError.Code);
        Assert.Equal(1, await setup.Db.TillCloses.CountAsync(x => x.ShopId == shop));
    }

    [Fact]
    public async Task PostingHoldsSession_CloseWaitsReloadsAndRejectsOldVersion()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var setup = new TillTestContext(fixture.MigratorConnectionString, shop);
        var dto = await setup.OpenAsync(shop, cashier, (await setup.CreateAsync(shop, owner)).Id);
        await using var source = new TillTestContext(fixture.MigratorConnectionString, shop);
        await using var sourceTx = await source.Db.Database.BeginTransactionAsync();
        Success(await source.Periods.AuthorizeForWriteAsync(shop, dto.BusinessDate));
        var locked = Success(await source.Posting.AcquireAsync(shop, cashier, new(dto.Id), dto.BusinessDate))!;
        Success(await source.Posting.AppendAsync(locked, new(TillMovementSource.OnlineSale, Guid.NewGuid(), "sale",
            [new(ReceiptTenderMethod.Cash, 500m, null, null, null, null)], 1m, source.Clock.UtcNow, "source-sale", null), cashier));
        await source.Db.SaveChangesAsync();
        await using var closer = new TillTestContext(fixture.MigratorConnectionString, shop);
        await closer.Db.Database.OpenConnectionAsync();
        var closerPid = await closer.Db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var closing = closer.Lifecycle.CloseAsync(new(shop, cashier, dto.Id, 1000m, null, dto.Version, "close-race"), default);
        await WaitForLockAsync(setup, closerPid);
        Assert.False(closing.IsCompleted);
        await sourceTx.CommitAsync();
        var result = await closing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Till.VersionConflict", result.FirstError.Code);
        var refreshed = Success(await closer.Reads.SessionAsync(shop, cashier, dto.Id, 1, 20, default)).Session;
        var final = Success(await closer.Lifecycle.CloseAsync(new(shop, cashier, dto.Id, 1500m, null, refreshed.Version, "close-fresh"), default));
        Assert.Equal(1500m, final.Close!.ExpectedCash);
        Assert.Equal(1, final.Close.Totals.MovementCount);
    }

    [Fact]
    public async Task ClosedSession_RefusesPostingAndKeepsFrozenCount()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var setup = new TillTestContext(fixture.MigratorConnectionString, shop);
        var dto = await setup.OpenAsync(shop, cashier, (await setup.CreateAsync(shop, owner)).Id);
        var closed = Success(await setup.Lifecycle.CloseAsync(new(shop, cashier, dto.Id, 1000m, null, dto.Version, "close-first"), default));
        await using var source = new TillTestContext(fixture.MigratorConnectionString, shop);
        await using var tx = await source.Db.Database.BeginTransactionAsync();
        Success(await source.Periods.AuthorizeForWriteAsync(shop, dto.BusinessDate));
        Assert.Equal("Till.SessionClosed", (await source.Posting.AcquireAsync(shop, cashier, new(dto.Id), dto.BusinessDate)).FirstError.Code);
        await tx.RollbackAsync();
        Assert.Equal(1000m, (await setup.Tills.GetCloseAsync(shop, dto.Id))!.ExpectedCash);
        Assert.Empty(await setup.Db.TillMovements.Where(x => x.ShopId == shop).ToArrayAsync());
        Assert.Equal(0, closed.Close!.LastMovementSequence);
    }

    [Fact]
    public async Task SessionContention_IsBoundedAndReturnsBusy()
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var setup = new TillTestContext(fixture.MigratorConnectionString, shop);
        var dto = await setup.OpenAsync(shop, cashier, (await setup.CreateAsync(shop, owner)).Id);
        await using var blocker = new TillTestContext(fixture.MigratorConnectionString, shop);
        await using var tx = await blocker.Db.Database.BeginTransactionAsync();
        Success(await blocker.Periods.AuthorizeForWriteAsync(shop, dto.BusinessDate));
        Success(await blocker.Posting.AcquireAsync(shop, cashier, new(dto.Id), dto.BusinessDate));
        await using var waiting = new TillTestContext(fixture.MigratorConnectionString, shop);
        var result = await waiting.Lifecycle.CloseAsync(new(shop, cashier, dto.Id, 1000m, null, dto.Version, "busy"), default).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Till.Busy", result.FirstError.Code);
        Assert.Empty(await setup.Db.OperationReceipts.Where(x => x.ShopId == shop && x.OperationKey == "busy").ToArrayAsync());
        await tx.RollbackAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PeriodCloseWins_TillLifecycleWaitsAndRefusesBeforeSession(bool opening)
    {
        var (shop, owner, cashier, _) = await SeedAsync(fixture.MigratorConnectionString);
        await using var setup = new TillTestContext(fixture.MigratorConnectionString, shop);
        var till = await setup.CreateAsync(shop, owner);
        var session = await setup.OpenAsync(shop, cashier, till.Id);
        var target = opening ? (await setup.CreateAsync(shop, owner, "Other")).Id : session.Id;
        await using var periodWriter = new TillTestContext(fixture.MigratorConnectionString, shop);
        await using var tx = await periodWriter.Db.Database.BeginTransactionAsync();
        await periodWriter.Db.Database.ExecuteSqlAsync($"UPDATE posting_periods SET state=2 WHERE shop_id={shop}");
        await using var lifecycle = new TillTestContext(fixture.MigratorConnectionString, shop);
        await lifecycle.Db.Database.OpenConnectionAsync();
        var pid = await lifecycle.Db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var pending = opening
            ? lifecycle.Lifecycle.OpenAsync(new(shop, cashier, target, cashier, 1000m, "period-open"), default)
            : lifecycle.Lifecycle.CloseAsync(new(shop, cashier, target, 1000m, null, session.Version, "period-close"), default);
        await WaitForLockAsync(setup, pid);
        Assert.False(pending.IsCompleted);
        await tx.CommitAsync();
        Assert.Equal("PostingPeriod.Closed", (await pending.WaitAsync(TimeSpan.FromSeconds(10))).FirstError.Code);
        Assert.Equal(1, await setup.Db.TillSessions.CountAsync(x => x.ShopId == shop));
        Assert.False(await setup.Db.TillCloses.AnyAsync(x => x.ShopId == shop));
        Assert.False(await setup.Db.TillMovements.AnyAsync(x => x.ShopId == shop));
    }

    private static async Task WaitForLockAsync(TillTestContext observer, int pid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await observer.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(timeout.Token))
            await Task.Delay(10, timeout.Token);
    }
}
