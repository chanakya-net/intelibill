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
            .Where(issue => issue.ShopId == shopId && issue.Status != ReconciliationCaseStatus.Resolved)
            .OrderByDescending(issue => issue.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<Guid?> FindOpenIssueIdAsync(
        Guid shopId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        Guid? offlineSubmissionId = null,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Where(issue => issue.ShopId == shopId
                && issue.ClientSaleId == clientSaleId
                && issue.DeviceId == deviceId
                && issue.IssueType == issueType
                && issue.Code == code
                && issue.Message == message
                && (offlineSubmissionId == null || issue.OfflineSubmissionId == offlineSubmissionId)
                && issue.Status != ReconciliationCaseStatus.Resolved)
            .OrderBy(issue => issue.CreatedAt)
            .Select(issue => (Guid?)issue.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ReconciliationIssue?> GetTrackedAsync(Guid shopId, Guid caseId, CancellationToken cancellationToken) =>
        await DbSet.FirstOrDefaultAsync(i => i.ShopId == shopId && i.Id == caseId, cancellationToken);

    public async Task<ReconciliationIssue?> GetAsync(Guid shopId, Guid caseId, CancellationToken cancellationToken) =>
        await DbSet.AsNoTracking().FirstOrDefaultAsync(i => i.ShopId == shopId && i.Id == caseId, cancellationToken);

    public async Task<(IReadOnlyList<ReconciliationIssue> Items, int TotalCount)> GetPageAsync(
        Guid shopId,
        ReconciliationCaseFilter filter,
        Guid? createdBy,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = DbSet.AsNoTracking().Where(i => i.ShopId == shopId);
        if (createdBy is { } creator)
            query = query.Where(i => i.CreatedBy == creator);

        query = filter switch
        {
            ReconciliationCaseFilter.Open => query.Where(i => i.Status != ReconciliationCaseStatus.Resolved),
            ReconciliationCaseFilter.Escalated => query.Where(i => i.Status == ReconciliationCaseStatus.Escalated),
            ReconciliationCaseFilter.Resolved => query.Where(i => i.Status == ReconciliationCaseStatus.Resolved),
            _ => query,
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<int> CountUnresolvedAsync(Guid shopId, Guid? createdBy, CancellationToken cancellationToken)
    {
        var query = DbSet.AsNoTracking()
            .Where(i => i.ShopId == shopId && i.Status != ReconciliationCaseStatus.Resolved);
        if (createdBy is { } creator)
            query = query.Where(i => i.CreatedBy == creator);

        return await query.CountAsync(cancellationToken);
    }

    public async Task<bool> IsOperationLinkedAsync(Guid shopId, Guid linkedOperationId, CancellationToken cancellationToken) =>
        await DbSet.AsNoTracking().AnyAsync(
            i => i.ShopId == shopId && i.LinkedOperationId == linkedOperationId,
            cancellationToken);
}
