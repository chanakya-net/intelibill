using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IExpenseCategoryRepository
{
    Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ExpenseCategory?> GetByNameAsync(Guid shopId, string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpenseCategory>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken);
    Task AddAsync(ExpenseCategory category, CancellationToken cancellationToken);
}
