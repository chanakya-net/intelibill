using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IExpenseRepository
{
    Task<RowLockOutcome> LockForCorrectionAsync(Guid shopId, Guid expenseId, CancellationToken ct = default);
    Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Expense>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Expense> Items, int TotalCount)> GetPagedByShopAsync(
        Guid shopId,
        string? searchTerm,
        ExpenseStatusFilter status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);
    Task AddAsync(Expense expense, CancellationToken cancellationToken);

    /// <summary>Expenses recorded as corrections of <paramref name="originalExpenseId"/> (one level).</summary>
    Task<IReadOnlyList<Expense>> GetCorrectionsOfAsync(Guid shopId, Guid originalExpenseId, CancellationToken cancellationToken);
    void Update(Expense expense);
    Task<IReadOnlyList<Expense>> GetByShopAndDateAsync(Guid shopId, DateOnly reportingDay, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Expense>> GetByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<decimal> GetSumByShopAndDateRangeAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExpenseDailyBucketReadModel>> GetDailyExpenseTrendAsync(Guid shopId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default);
}

public sealed record ExpenseDailyBucketReadModel(DateOnly Date, decimal TotalAmount);
