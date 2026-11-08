using Npgsql;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

public sealed class ExpenseRepository(ApplicationDbContext context) : IExpenseRepository
{
    public async Task<RowLockOutcome> LockForCorrectionAsync(Guid shopId, Guid expenseId, CancellationToken ct = default)
    {
        if (!context.Database.IsNpgsql()) return RowLockOutcome.Acquired;
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Expense lock requires a transaction.");
        var previous = await context.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync(ct);
        await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {"5s"}, true)", ct);
        try
        {
            var ids = await context.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM expenses WHERE shop_id={shopId} AND id={expenseId} FOR NO KEY UPDATE").ToListAsync(ct);
            await context.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {previous}, true)", ct);
            if (ids.Count == 0) return RowLockOutcome.NotFound;
            var expense = await context.Expenses.SingleAsync(e => e.Id == expenseId && e.ShopId == shopId, ct);
            await context.Entry(expense).ReloadAsync(ct);
            return RowLockOutcome.Acquired;
        }
        catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return RowLockOutcome.TimedOut;
        }
    }

    public async Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Expenses
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Expense>> GetCorrectionsOfAsync(Guid shopId, Guid originalExpenseId, CancellationToken cancellationToken) =>
        await context.Expenses
            .Where(e => e.ShopId == shopId && e.OriginalExpenseId == originalExpenseId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Expense>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken) =>
        await context.Expenses
            .Include(e => e.Category)
            .Where(e => e.ShopId == shopId)
            .OrderByDescending(e => e.ExpenseDate)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Expense> Items, int TotalCount)> GetPagedByShopAsync(
        Guid shopId,
        string? searchTerm,
        ExpenseStatusFilter status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = context.Expenses
            .Include(e => e.Category)
            .Where(e => e.ShopId == shopId)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLowerInvariant();
            query = query.Where(e =>
                EF.Functions.ILike(e.PaidTo, $"%{term}%") ||
                EF.Functions.ILike(e.Description!, $"%{term}%") ||
                EF.Functions.ILike(e.Category.Name, $"%{term}%"));
        }

        if (status != ExpenseStatusFilter.All)
        {
            query = query.Where(e => e.IsVoided == (status == ExpenseStatusFilter.Voided));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddAsync(Expense expense, CancellationToken cancellationToken) =>
        await context.Expenses.AddAsync(expense, cancellationToken);

    public void Update(Expense expense) =>
        context.Expenses.Update(expense);

    public async Task<IReadOnlyList<Expense>> GetByShopAndDateAsync(Guid shopId, DateOnly reportingDay, CancellationToken cancellationToken = default) =>
        await context.Expenses
            .Where(e => e.ShopId == shopId && e.ExpenseDate == reportingDay && !e.IsVoided)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Expense>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
        await context.Expenses
            .Where(e => e.ShopId == shopId && e.ExpenseDate >= startDate && e.ExpenseDate <= endDate && !e.IsVoided)
            .ToListAsync(cancellationToken);

    public async Task<decimal> GetSumByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
        await context.Expenses
            .Where(e => e.ShopId == shopId && e.ExpenseDate >= startDate && e.ExpenseDate <= endDate && !e.IsVoided)
            .SumAsync(e => e.Amount, cancellationToken);

    public async Task<IReadOnlyList<ExpenseDailyBucketReadModel>> GetDailyExpenseTrendAsync(
        Guid shopId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default) =>
        (await context.Expenses
            .AsNoTracking()
            .Where(e => e.ShopId == shopId && e.ExpenseDate >= startDate && e.ExpenseDate <= endDate && !e.IsVoided)
            .GroupBy(e => e.ExpenseDate)
            .Select(group => new ExpenseDailyBucketReadModel(group.Key, group.Sum(expense => expense.Amount)))
            .ToListAsync(cancellationToken))
        .OrderBy(bucket => bucket.Date)
        .ToList();
}
