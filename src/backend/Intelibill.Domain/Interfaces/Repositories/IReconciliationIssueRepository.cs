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

    /// <summary>
    /// The oldest open case for the same hold. When <paramref name="offlineSubmissionId"/> is given, only a case
    /// filed for that envelope matches, so a changed resend never inherits another payload's case.
    /// </summary>
    Task<Guid?> FindOpenIssueIdAsync(
        Guid shopId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        Guid? offlineSubmissionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The oldest open legacy case for the same hold: same shop, client sale id, device, type, code and
    /// message, filed before envelopes existed (no envelope). Tracked, so the caller can attach the
    /// envelope that now evidences it instead of filing a duplicate.
    /// </summary>
    Task<ReconciliationIssue?> FindOpenLegacyIssueAsync(
        Guid shopId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>The oldest unresolved (open or escalated) case filed for the envelope, whatever its reason; untracked.</summary>
    Task<ReconciliationIssue?> FindUnresolvedByEnvelopeAsync(
        Guid shopId,
        Guid offlineSubmissionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the case row until the current transaction ends, so resolutions of one case queue
    /// behind each other and each reads the case as the previous one committed it.
    /// </summary>
    Task<RowLockOutcome> LockForResolutionAsync(Guid shopId, Guid caseId, CancellationToken cancellationToken);

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
