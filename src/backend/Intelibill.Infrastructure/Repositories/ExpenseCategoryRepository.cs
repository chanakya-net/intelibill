using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

public sealed class ExpenseCategoryRepository(ApplicationDbContext context) : IExpenseCategoryRepository
{
    public async Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.ExpenseCategories.FindAsync([id], cancellationToken);

    public async Task<ExpenseCategory?> GetByNameAsync(Guid shopId, string name, CancellationToken cancellationToken) =>
        await context.ExpenseCategories
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Name == name, cancellationToken);

    public async Task<IReadOnlyList<ExpenseCategory>> GetByShopAsync(Guid shopId, CancellationToken cancellationToken) =>
        await context.ExpenseCategories
            .Where(c => c.ShopId == shopId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ExpenseCategory category, CancellationToken cancellationToken) =>
        await context.ExpenseCategories.AddAsync(category, cancellationToken);
}
