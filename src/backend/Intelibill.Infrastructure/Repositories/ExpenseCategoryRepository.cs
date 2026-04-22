using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ExpenseCategoryRepository(ApplicationDbContext context)
    : RepositoryBase<ExpenseCategory>(context), IExpenseCategoryRepository
{
    public Task<ExpenseCategory?> GetByNameAsync(Guid shopId, string name, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<ExpenseCategory>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
