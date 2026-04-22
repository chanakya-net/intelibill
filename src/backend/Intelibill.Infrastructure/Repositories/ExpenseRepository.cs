using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ExpenseRepository(ApplicationDbContext context)
    : RepositoryBase<Expense>(context), IExpenseRepository
{
    public Task<IReadOnlyList<Expense>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<(IReadOnlyList<Expense> Items, int TotalCount)> GetPagedByShopAsync(
        Guid shopId,
        string? searchTerm,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
