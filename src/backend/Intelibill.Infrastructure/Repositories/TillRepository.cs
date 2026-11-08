using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class TillRepository(ApplicationDbContext context) : ITillRepository
{
    public Task<Till?> GetTillAsync(Guid shopId, Guid tillId, CancellationToken ct = default) =>
        context.Tills.SingleOrDefaultAsync(x => x.ShopId == shopId && x.Id == tillId, ct);
    public async Task<IReadOnlyList<Till>> ListTillsAsync(Guid shopId, CancellationToken ct = default) =>
        await context.Tills.AsNoTracking().Where(x => x.ShopId == shopId).OrderBy(x => x.NormalizedName).ToListAsync(ct);
    public Task<TillSession?> GetSessionAsync(Guid shopId, Guid sessionId, CancellationToken ct = default) =>
        context.TillSessions.SingleOrDefaultAsync(x => x.ShopId == shopId && x.Id == sessionId, ct);
    public Task<TillSession?> GetOpenSessionAsync(Guid shopId, Guid tillId, CancellationToken ct = default) =>
        context.TillSessions.SingleOrDefaultAsync(x => x.ShopId == shopId && x.TillId == tillId && x.State == TillSessionState.Open, ct);

    public async Task<(IReadOnlyList<TillSession> Items, int TotalCount)> ListSessionsAsync(Guid shopId, Guid? cashierId,
        TillSessionState? state, DateOnly? businessDate, int page, int pageSize, CancellationToken ct = default)
    {
        ValidatePage(page, pageSize);
        var query = context.TillSessions.AsNoTracking().Where(x => x.ShopId == shopId);
        if (cashierId.HasValue) query = query.Where(x => x.CashierId == cashierId.Value);
        if (state.HasValue) query = query.Where(x => x.State == state.Value);
        if (businessDate.HasValue) query = query.Where(x => x.BusinessDate == businessDate.Value);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.OpenedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (rows, count);
    }

    public Task<RowLockOutcome> LockCatalogAsync(Guid shopId, CancellationToken ct = default) => LockAsync("shops", shopId, shopId, ct);
    public Task<RowLockOutcome> LockTillAsync(Guid shopId, Guid tillId, CancellationToken ct = default) => LockAsync("tills", shopId, tillId, ct);
    public Task<RowLockOutcome> LockSessionAsync(Guid shopId, Guid sessionId, CancellationToken ct = default) => LockAsync("till_sessions", shopId, sessionId, ct);

    private async Task<RowLockOutcome> LockAsync(string table, Guid shopId, Guid id, CancellationToken ct)
    {
        if (!context.Database.IsNpgsql() || context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Till row locks require a PostgreSQL transaction.");
        var previous = (await context.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").ToListAsync(ct)).Single();
        await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {"5s"}, true)", ct);
        try
        {
            // table is a private server-owned constant, never a client-provided identifier.
            FormattableString sql;
            if (table == "shops") sql = $"SELECT id AS \"Value\" FROM shops WHERE id = {shopId} FOR NO KEY UPDATE";
            else if (table == "tills") sql = $"SELECT id AS \"Value\" FROM tills WHERE shop_id = {shopId} AND id = {id} FOR NO KEY UPDATE";
            else sql = $"SELECT id AS \"Value\" FROM till_sessions WHERE shop_id = {shopId} AND id = {id} FOR NO KEY UPDATE";
            var rows = await context.Database.SqlQuery<Guid>(sql).ToListAsync(ct);
            await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {previous}, true)", ct);
            return rows.Count == 0 ? RowLockOutcome.NotFound : RowLockOutcome.Acquired;
        }
        catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            // PostgreSQL aborts the transaction: the caller must roll back; SET cannot execute here.
            return RowLockOutcome.TimedOut;
        }
    }

    public async Task<TillSession?> ReloadSessionAsync(Guid shopId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await GetSessionAsync(shopId, sessionId, ct);
        if (session is null) return null;
        await context.Entry(session).ReloadAsync(ct);
        return context.Entry(session).State == EntityState.Detached ? null : session;
    }

    public async Task<TillTotals> GetTotalsAsync(Guid shopId, Guid sessionId, CancellationToken ct = default)
    {
        var movements = await context.TillMovements.AsNoTracking().Where(x => x.ShopId == shopId && x.SessionId == sessionId).ToListAsync(ct);
        var sources = await context.TillSourceRecords.AsNoTracking().Where(x => x.ShopId == shopId && x.SessionId == sessionId).ToListAsync(ct);
        return TillTotals.From(movements, sources);
    }

    public async Task<(IReadOnlyList<TillMovement> Items, int TotalCount)> GetMovementsAsync(Guid shopId, Guid sessionId, int page, int pageSize, CancellationToken ct = default)
    {
        ValidatePage(page, pageSize);
        var query = context.TillMovements.AsNoTracking().Where(x => x.ShopId == shopId && x.SessionId == sessionId);
        var count = await query.CountAsync(ct);
        return (await query.OrderBy(x => x.Sequence).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), count);
    }

    public async Task<(IReadOnlyList<TillSourceRecord> Items, int TotalCount)> GetSourcesAsync(Guid shopId, Guid sessionId, int page, int pageSize, CancellationToken ct = default)
    {
        ValidatePage(page, pageSize);
        var query = context.TillSourceRecords.AsNoTracking().Where(x => x.ShopId == shopId && x.SessionId == sessionId);
        var count = await query.CountAsync(ct);
        return (await query.OrderBy(x => x.RecordedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), count);
    }

    public async Task<TillSourceRecord?> FindSourceAsync(Guid shopId, TillMovementSource sourceType, Guid sourceId, string purpose, CancellationToken ct = default) =>
        context.TillSourceRecords.Local.FirstOrDefault(x => x.ShopId == shopId && x.SourceType == sourceType && x.SourceId == sourceId && x.Purpose == purpose)
        ?? await context.TillSourceRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId && x.SourceType == sourceType && x.SourceId == sourceId && x.Purpose == purpose, ct);
    public Task<TillClose?> GetCloseAsync(Guid shopId, Guid sessionId, CancellationToken ct = default) =>
        context.TillCloses.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId && x.SessionId == sessionId, ct);
    public Task<TillCloseReview?> GetReviewAsync(Guid shopId, Guid closeId, CancellationToken ct = default) =>
        context.TillCloseReviews.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId && x.CloseId == closeId, ct);
    public async Task<IReadOnlyList<TillLatePosting>> GetLatePostingsAsync(Guid shopId, Guid sessionId, CancellationToken ct = default) =>
        await context.TillLatePostings.AsNoTracking().Where(x => x.ShopId == shopId && x.SessionId == sessionId).OrderBy(x => x.RecordedAt).ToListAsync(ct);
    public Task<TillLatePosting?> FindLatePostingAsync(Guid shopId, Guid submissionId, Guid saleId, CancellationToken ct = default) =>
        context.TillLatePostings.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId && (x.OfflineSubmissionId == submissionId || x.SaleId == saleId), ct);
    public async Task AddTillAsync(Till till, CancellationToken ct = default) => await context.Tills.AddAsync(till, ct);
    public async Task AddSessionAsync(TillSession session, CancellationToken ct = default) => await context.TillSessions.AddAsync(session, ct);
    public async Task AddSourceAsync(TillSourceRecord source, CancellationToken ct = default) => await context.TillSourceRecords.AddAsync(source, ct);
    public async Task AddMovementAsync(TillMovement movement, CancellationToken ct = default) => await context.TillMovements.AddAsync(movement, ct);
    public async Task AddCloseAsync(TillClose close, CancellationToken ct = default) => await context.TillCloses.AddAsync(close, ct);
    public async Task AddReviewAsync(TillCloseReview review, CancellationToken ct = default) => await context.TillCloseReviews.AddAsync(review, ct);
    public async Task AddLatePostingAsync(TillLatePosting latePosting, CancellationToken ct = default) => await context.TillLatePostings.AddAsync(latePosting, ct);
    private static void ValidatePage(int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, int.MaxValue / pageSize);
    }
}
