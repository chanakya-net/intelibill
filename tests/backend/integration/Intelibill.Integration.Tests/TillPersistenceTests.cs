using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class TillPersistenceTests(PostgreSqlTestFixture fixture)
{
    private static readonly string[] Tables = ["tills", "till_sessions", "till_source_records", "till_movements", "till_closes", "till_close_reviews", "till_late_postings"];

    [Fact]
    public async Task Migration_EnforcesTenantPoliciesOnEveryTillTable()
    {
        await using var owner = new NpgsqlConnection(fixture.MigratorConnectionString);
        await owner.OpenAsync();
        foreach (var table in Tables)
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_class c JOIN pg_policy p ON p.polrelid=c.oid WHERE c.relname=@table AND c.relrowsecurity AND c.relforcerowsecurity AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL", owner);
            command.Parameters.AddWithValue("table", table);
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Persistence_FreezesCloseAndUsesXminAndTenantIsolation()
    {
        await using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var today = new DateOnly(2026, 11, 8);
        var shop = Shop.Create("Till test", "address", "city", "state", "560001", null, null, null);
        var user = User.CreateWithEmail($"till-{Guid.NewGuid():N}@example.test", "hash", "Till", "Cashier");
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, false);
        context.AddRange(shop, user, membership);
        await context.SaveChangesAsync();
        var till = Till.Create(shop.Id, "Front", user.Id, now).Value;
        var session = TillSession.Open(shop.Id, till.Id, user.Id, till.Name, "Till Cashier", user.Id, now, today, "Asia/Kolkata", 1000m, null).Value;
        context.AddRange(till, session);
        await context.SaveChangesAsync();
        var version = session.Version;
        var source = TillSourceRecord.Record(session, TillMovementSource.OnlineSale, Guid.NewGuid(), "sale", "op", user.Id, now, now, null, null, new(0, 0, 20m)).Value;
        var movement = TillMovement.Record(session, source, 1, new(ReceiptTenderMethod.Cash, 300m, 500m, 200m, null, null), 1m, user.Id, now, null).Value;
        context.AddRange(source, movement);
        await context.SaveChangesAsync();
        Assert.NotEqual(version, session.Version);
        var duplicate = TillSourceRecord.Record(session, TillMovementSource.OnlineSale, source.SourceId, "sale", "other-operation", user.Id, now, now, null, null, new()).Value;
        context.Add(duplicate);
        var replayFailure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)replayFailure.InnerException!).SqlState);
        context.Entry(duplicate).State = EntityState.Detached;
        await context.Entry(session).ReloadAsync();
        var repository = new TillRepository(context);
        var totals = await repository.GetTotalsAsync(shop.Id, session.Id);
        var close = session.Close(totals, 1290m, user.Id, now, today, "short ten", true, false).Value;
        var review = TillCloseReview.Record(close, user.Id, now, "count acknowledged").Value;
        context.AddRange(close, review);
        await context.SaveChangesAsync();
        var submission = OfflineSubmission.Record(shop.Id, "device", "late-1", "LATE/1", "hash", "{}", now, now,
            OfflineSubmissionSource.Sync, user.Id, null, user.Id, null, null);
        var sale = Sale.Create(shop.Id, user.Id, "late-sale", "hash", "LATE/1", null, null, null, PaymentMethod.Credit,
            now, BusinessDay.InDefaultZone(now), 0m, 0m, 0m, 0m, []);
        var issue = ReconciliationIssue.Create(shop.Id, sale.Id, "late-1", "device", ReconciliationIssueType.StockVariance,
            "late", "review", user.Id, offlineSubmissionId: submission.Id);
        var late = TillLatePosting.Record(shop.Id, submission.Id, sale.Id, issue.Id, session.Id, close.Id, today,
            now, now, user.Id, [], "Unknown", "legacy tender unavailable").Value;
        context.AddRange(submission, sale, issue, late);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var stored = await repository.GetCloseAsync(shop.Id, session.Id);
        Assert.Equal(1300m, stored!.ExpectedCash);
        Assert.Equal(-10m, stored.Variance);
        Assert.Equal(20m, stored.Totals.ReceivableChange);
        Assert.Equal(300m, stored.Totals.RecordedTenderTotals[ReceiptTenderMethod.Cash]);
        var storedLate = Assert.Single(await repository.GetLatePostingsAsync(shop.Id, session.Id));
        Assert.False(storedLate.IncludedInCount);
        Assert.Empty(storedLate.TenderLines);
        Assert.Empty((await repository.GetMovementsAsync(Guid.NewGuid(), session.Id, 1, 10)).Items);

        await using var runtime = new NpgsqlConnection(fixture.RuntimeConnectionString);
        await runtime.OpenAsync();
        foreach (var table in Tables)
        {
            await using var hidden = new NpgsqlCommand($"SELECT count(*) FROM {table}", runtime);
            Assert.Equal(0L, (long)(await hidden.ExecuteScalarAsync())!);
        }
        await using (var scope = new NpgsqlCommand("SELECT set_config('app.active_shop_id', @shop, false)", runtime))
        { scope.Parameters.AddWithValue("shop", shop.Id.ToString()); await scope.ExecuteNonQueryAsync(); }
        foreach (var table in Tables)
        {
            await using var visible = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE shop_id=@shop", runtime);
            visible.Parameters.AddWithValue("shop", shop.Id);
            Assert.Equal(1L, (long)(await visible.ExecuteScalarAsync())!);
        }
        await using var mutation = new NpgsqlCommand("UPDATE till_closes SET counted_cash = 1 WHERE id=@id", runtime);
        mutation.Parameters.AddWithValue("id", close.Id);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync())).SqlState);
    }

    [Fact]
    public async Task Constraints_RejectDuplicateOpenCrossTenantAndExcessPrecision()
    {
        await using var context = CreateContext();
        var (shop, user, till, session) = await SeedSessionAsync(context);
        var second = TillSession.Open(shop.Id, till.Id, user.Id, "Front", "Cashier", user.Id, session.OpenedAt,
            session.BusinessDate, "Asia/Kolkata", 0m, null).Value;
        context.Add(second);
        var duplicateOpen = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)duplicateOpen.InnerException!).SqlState);
        context.Entry(second).State = EntityState.Detached;
        var foreignShop = Shop.Create("Foreign", "address", "city", "state", "560001", null, null, null);
        var foreignTill = Till.Create(foreignShop.Id, "Other", user.Id, session.OpenedAt).Value;
        context.AddRange(foreignShop, ShopMembership.Create(foreignShop.Id, user.Id, ShopRole.Owner, false), foreignTill);
        await context.SaveChangesAsync();
        var crossShop = TillSession.Open(shop.Id, foreignTill.Id, user.Id, "Other", "Cashier", user.Id, session.OpenedAt,
            session.BusinessDate, "Asia/Kolkata", 0m, null).Value;
        context.Add(crossShop);
        var foreignKey = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)foreignKey.InnerException!).SqlState);
        context.Entry(crossShop).State = EntityState.Detached;
        var precision = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"UPDATE till_sessions SET opening_float = 1.001 WHERE id = {session.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, precision.SqlState);
    }

    [Fact]
    public async Task RepositoryLock_RequiresTransactionReloadsXminAndRestoresTimeout()
    {
        await using var first = CreateContext();
        var (shop, user, _, stale) = await SeedSessionAsync(first);
        var oldVersion = stale.Version;
        var firstRepository = new TillRepository(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => firstRepository.LockSessionAsync(shop.Id, stale.Id));
        await using var second = CreateContext();
        var secondSession = (await new TillRepository(second).GetSessionAsync(shop.Id, stale.Id))!;
        var source = TillSourceRecord.Record(secondSession, TillMovementSource.OnlineSale, Guid.NewGuid(), "credit-only", "op", user.Id,
            stale.OpenedAt, stale.OpenedAt, null, null, new(0m, 0m, 50m)).Value;
        second.Add(source);
        await second.SaveChangesAsync();
        await using var transaction = await first.Database.BeginTransactionAsync();
        await first.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {"2s"}, true)");
        Assert.Equal(Intelibill.Domain.Interfaces.Repositories.RowLockOutcome.Acquired,
            await firstRepository.LockSessionAsync(shop.Id, stale.Id));
        var fresh = await firstRepository.ReloadSessionAsync(shop.Id, stale.Id);
        Assert.NotEqual(oldVersion, fresh!.Version);
        Assert.Equal(1, fresh.SourceCount);
        Assert.Equal("2s", await first.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync());
        await transaction.RollbackAsync();
    }

    private static async Task<(Shop Shop, User User, Till Till, TillSession Session)> SeedSessionAsync(ApplicationDbContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var shop = Shop.Create("Till constraints", "address", "city", "state", "560001", null, null, null);
        var user = User.CreateWithEmail($"till-{Guid.NewGuid():N}@example.test", "hash", "Till", "Cashier");
        var till = Till.Create(shop.Id, "Front", user.Id, now).Value;
        var session = TillSession.Open(shop.Id, till.Id, user.Id, till.Name, "Till Cashier", user.Id, now,
            new DateOnly(2026, 11, 8), "Asia/Kolkata", 1000m, null).Value;
        context.AddRange(shop, user, ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, false), till, session);
        await context.SaveChangesAsync();
        return (shop, user, till, session);
    }

    private ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(fixture.MigratorConnectionString).UseSnakeCaseNamingConvention().Options);
}
