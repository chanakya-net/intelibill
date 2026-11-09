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
    public Task<ShopTillSetting?> GetSettingsAsync(Guid shopId, CancellationToken ct = default) =>
        context.ShopTillSettings.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shopId, ct);
    public async Task SaveSettingsAsync(ShopTillSetting settings, CancellationToken ct = default)
    {
        var tracked = context.ShopTillSettings.Local.FirstOrDefault(x => x.ShopId == settings.ShopId);
        if (tracked is not null) context.Entry(tracked).State = EntityState.Detached;
        if (settings.Version == 1 && !await context.ShopTillSettings.AnyAsync(x => x.ShopId == settings.ShopId, ct))
            await context.ShopTillSettings.AddAsync(settings, ct);
        else
        {
            context.Attach(settings);
            context.Entry(settings).Property(x => x.Enabled).IsModified = true;
            context.Entry(settings).Property(x => x.Version).OriginalValue = settings.Version - 1;
            context.Entry(settings).Property(x => x.Version).IsModified = true;
        }
    }
    public async Task AddSettingsEventAsync(ShopTillSettingEvent audit, CancellationToken ct = default) => await context.ShopTillSettingEvents.AddAsync(audit, ct);
    public async Task<(int Open, int AwaitingReview, int PendingOffline)> GetSettingsBlockersAsync(Guid shopId, CancellationToken ct = default)
    {
        var open = await context.TillSessions.CountAsync(x => x.ShopId == shopId && x.State == TillSessionState.Open, ct);
        var review = await context.TillCloses.CountAsync(x => x.ShopId == shopId && x.AwaitingReview &&
            !context.TillCloseReviews.Any(r => r.ShopId == shopId && r.CloseId == x.Id), ct);
        // An envelope resolved by linking/acknowledgement need not have SaleId. Count each unposted
        // envelope once, plus pre-envelope unposted cases; terminal dispositions are not unresolved queues.
        var envelopes = await context.OfflineSubmissions.CountAsync(x => x.ShopId == shopId && x.SaleId == null &&
            (!context.ReconciliationIssues.Any(r => r.ShopId == shopId && r.OfflineSubmissionId == x.Id) ||
             context.ReconciliationIssues.Any(r => r.ShopId == shopId && r.OfflineSubmissionId == x.Id && r.Status != ReconciliationCaseStatus.Resolved)), ct);
        var cases = await context.ReconciliationIssues.CountAsync(x => x.ShopId == shopId && x.Status != ReconciliationCaseStatus.Resolved &&
            ((x.OfflineSubmissionId == null && (x.SaleId == null || x.IssueType == ReconciliationIssueType.TillReview)) ||
             (x.IssueType == ReconciliationIssueType.TillReview && context.OfflineSubmissions.Any(e => e.ShopId == shopId && e.Id == x.OfflineSubmissionId && e.SaleId != null))), ct);
        return (open, review, envelopes + cases);
    }
    public Task<RowLockOutcome> LockSettingsActorAsync(Guid actorId, CancellationToken ct = default) => LockAsync("actor", actorId, actorId, ct);
    public Task<RowLockOutcome> LockSettingsReadAsync(Guid shopId, CancellationToken ct = default) => LockAsync("shop_gate", shopId, shopId, ct);

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
            if (table == "actor") sql = $"SELECT id AS \"Value\" FROM users WHERE id = {id} FOR SHARE";
            else if (table == "shop_gate") sql = $"SELECT id AS \"Value\" FROM shops WHERE id = {shopId} FOR SHARE";
            else if (table == "shops") sql = $"SELECT id AS \"Value\" FROM shops WHERE id = {shopId} FOR NO KEY UPDATE";
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

    public async Task<IReadOnlyDictionary<(TillMovementSource Type, Guid Id), TillSourceReference>> GetSourceReferencesAsync(
        Guid shopId, IReadOnlyCollection<(TillMovementSource Type, Guid Id)> sources, bool includeFinancial, CancellationToken ct = default)
    {
        var result = new Dictionary<(TillMovementSource Type, Guid Id), TillSourceReference>();
        foreach (var group in sources.Distinct().GroupBy(x => x.Type))
        {
            var ids = group.Select(x => x.Id).ToArray();
            var type = group.Key;
            if (type is TillMovementSource.OnlineSale or TillMovementSource.OfflineSale)
            {
                foreach (var id in await context.Sales.AsNoTracking().Where(x => x.ShopId == shopId && ids.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(ct))
                    result[(type, id)] = new(ParentSaleId: id);
                continue;
            }
            if (!includeFinancial) continue;
            if (type is TillMovementSource.SaleReturn or TillMovementSource.SaleReturnVoid)
            {
                foreach (var row in await context.SaleReturns.AsNoTracking().Where(x => x.ShopId == shopId && ids.Contains(x.Id)).Select(x => new { x.Id, x.SaleId }).ToArrayAsync(ct))
                    result[(type, row.Id)] = new(ParentSaleId: row.SaleId);
            }
            else if (type == TillMovementSource.SaleCancellation)
            {
                foreach (var row in await context.SaleCancellations.AsNoTracking().Where(x => x.ShopId == shopId && ids.Contains(x.Id)).Select(x => new { x.Id, x.SaleId }).ToArrayAsync(ct))
                    result[(type, row.Id)] = new(ParentSaleId: row.SaleId);
            }
            else if (type is TillMovementSource.CustomerPayment or TillMovementSource.SupplierPayment)
            {
                var party = type == TillMovementSource.CustomerPayment ? PaymentPartyType.Customer : PaymentPartyType.Supplier;
                foreach (var id in await context.PaymentReceipts.AsNoTracking().Where(x => x.ShopId == shopId && x.PartyType == party && ids.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(ct))
                    result[(type, id)] = new(PaymentReceiptId: id);
            }
            else if (type is TillMovementSource.CustomerPaymentReversal or TillMovementSource.SupplierPaymentReversal)
            {
                var party = type == TillMovementSource.CustomerPaymentReversal ? PaymentPartyType.Customer : PaymentPartyType.Supplier;
                var rows = await (from reversal in context.PaymentReversals.AsNoTracking()
                                  join receipt in context.PaymentReceipts.AsNoTracking() on reversal.ReceiptId equals receipt.Id
                                  where reversal.ShopId == shopId && receipt.ShopId == shopId && receipt.PartyType == party && ids.Contains(reversal.Id)
                                  select new { reversal.Id, reversal.ReceiptId }).ToArrayAsync(ct);
                foreach (var row in rows) result[(type, row.Id)] = new(PaymentReceiptId: row.ReceiptId);
            }
            else if (type is TillMovementSource.Expense or TillMovementSource.ExpenseCorrection)
            {
                foreach (var id in await context.Expenses.AsNoTracking().Where(x => x.ShopId == shopId && ids.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(ct))
                    result[(type, id)] = new(ExpenseId: id);
            }
        }
        return result;
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
