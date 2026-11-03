using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Interfaces.Repositories;

public enum ReconciliationCaseFilter
{
    /// <summary>Open and escalated.</summary>
    Open = 1,
    Escalated = 2,
    Resolved = 3,
    All = 4,
}

public interface IReconciliationIssueRepository : IRepository<ReconciliationIssue>
{
    Task<IReadOnlyList<ReconciliationIssue>> GetUnresolvedByShopAsync(
        Guid shopId,
        CancellationToken cancellationToken = default);

    Task<Guid?> FindOpenIssueIdAsync(
        Guid shopId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        CancellationToken cancellationToken = default);

    Task<ReconciliationIssue?> GetTrackedAsync(Guid shopId, Guid caseId, CancellationToken cancellationToken);

    Task<ReconciliationIssue?> GetAsync(Guid shopId, Guid caseId, CancellationToken cancellationToken);

    /// <summary>One page of cases, newest first; <paramref name="createdBy"/> limits to one creator.</summary>
    Task<(IReadOnlyList<ReconciliationIssue> Items, int TotalCount)> GetPageAsync(
        Guid shopId,
        ReconciliationCaseFilter filter,
        Guid? createdBy,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<int> CountUnresolvedAsync(Guid shopId, Guid? createdBy, CancellationToken cancellationToken);

    Task<bool> IsOperationLinkedAsync(Guid shopId, Guid linkedOperationId, CancellationToken cancellationToken);
}
