using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IExpenseRepository
{
    Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Expense>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Expense> Items, int TotalCount)> GetPagedByShopAsync(
        Guid shopId,
        string? searchTerm,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);
    Task AddAsync(Expense expense, CancellationToken cancellationToken);
    void Update(Expense expense);
    Task<IReadOnlyList<Expense>> GetByShopAndDateAsync(Guid shopId, DateOnly reportingDay, CancellationToken cancellationToken = default);
}
