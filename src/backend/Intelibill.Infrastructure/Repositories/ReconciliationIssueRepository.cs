using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
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

    public async Task<Guid?> FindOpenIssueIdAsync(
        Guid shopId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Where(issue => issue.ShopId == shopId
                && issue.ClientSaleId == clientSaleId
                && issue.DeviceId == deviceId
                && issue.IssueType == issueType
                && issue.Code == code
                && issue.Message == message
                && !issue.IsResolved)
            .OrderBy(issue => issue.CreatedAt)
            .Select(issue => (Guid?)issue.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
