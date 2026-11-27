using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ReconciliationCaseEventRepository(ApplicationDbContext context)
    : RepositoryBase<ReconciliationCaseEvent>(context), IReconciliationCaseEventRepository
{
    public async Task<IReadOnlyList<ReconciliationCaseEvent>> GetByCaseAsync(
        Guid shopId,
        Guid caseId,
        CancellationToken cancellationToken) =>
        await DbSet
            .AsNoTracking()
            .Where(e => e.ShopId == shopId && e.CaseId == caseId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
}
