using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ReconciliationIssueRepository(ApplicationDbContext context)
    : RepositoryBase<ReconciliationIssue>(context), IReconciliationIssueRepository
{
    public async Task<IReadOnlyList<ReconciliationIssue>> GetUnresolvedByShopAsync(
        Guid shopId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Where(issue => issue.ShopId == shopId && !issue.IsResolved)
            .OrderByDescending(issue => issue.CreatedAt)
            .ToListAsync(cancellationToken);
}
