using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

public sealed class ExpenseRepository(ApplicationDbContext context) : IExpenseRepository
{
    public async Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Expenses
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Expense>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken) =>
        await context.Expenses
            .Include(e => e.Category)
            .Where(e => e.ShopId == shopId)
            .OrderByDescending(e => e.ExpenseDate)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Expense> Items, int TotalCount)> GetPagedByShopAsync(
        Guid shopId,
        string? searchTerm,
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

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAt)
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
}
