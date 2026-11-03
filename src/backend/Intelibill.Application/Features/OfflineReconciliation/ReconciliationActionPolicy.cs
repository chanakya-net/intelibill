using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.OfflineReconciliation;

/// <summary>
/// The allowlist of resolution actions: what a person in a given role may do to a given case. The
/// resolve command refuses anything this does not offer, so the console and the server agree.
/// </summary>
/// <remarks>
/// <para>
/// Owners and managers get the actions the case's evidence supports, plus <c>Escalate</c> while it
/// is open. Staff may only escalate an open case they filed. A resolved case offers nothing.
/// </para>
/// <para>
/// There is deliberately no generic edit, renumber or delete: every resolution either links
/// existing evidence or replays the frozen envelope unchanged.
/// </para>
/// </remarks>
public static class ReconciliationActionPolicy
{
    private static readonly HashSet<string> ReplayableHoldCodes = new(StringComparer.Ordinal)
    {
        Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code,
        Errors.OfflineReconciliation.RecoveryReviewRequired.Code,
        Errors.OfflineReconciliation.IssuerNotSubmitter.Code,
    };

    private static readonly HashSet<string> SaleConflictCodes = new(StringComparer.Ordinal)
    {
        Errors.Sale.IdempotencyConflict.Code,
        Errors.Sale.InvoiceNumberAlreadyUsed.Code,
    };

    /// <param name="issue">The case.</param>
    /// <param name="submission">The case's envelope, or null when it has none (legacy cases).</param>
    /// <param name="role">The caller's live role in the case's shop.</param>
    /// <param name="actorUserId">The caller.</param>
    public static IReadOnlyList<ReconciliationAction> AvailableActions(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ShopRole role,
        Guid actorUserId)
    {
        if (issue.Status == ReconciliationCaseStatus.Resolved)
            return [];

        if (role is not (ShopRole.Owner or ShopRole.Manager))
        {
            return issue.Status == ReconciliationCaseStatus.Open && issue.CreatedBy == actorUserId
                ? [ReconciliationAction.Escalate]
                : [];
        }

        var actions = new List<ReconciliationAction>(4);
        actions.AddRange(ResolutionsFor(issue, submission));
        if (issue.Status == ReconciliationCaseStatus.Open)
            actions.Add(ReconciliationAction.Escalate);

        return actions;
    }

    private static IEnumerable<ReconciliationAction> ResolutionsFor(ReconciliationIssue issue, OfflineSubmission? submission)
    {
        switch (issue.IssueType)
        {
            case ReconciliationIssueType.PricingVariance:
            case ReconciliationIssueType.DiscountVariance:
            case ReconciliationIssueType.CustomerVariance:
                yield return ReconciliationAction.AcknowledgeVariance;
                yield break;

            case ReconciliationIssueType.StockVariance:
                if (issue.ItemId is not null && issue.InventoryBatchId is not null && issue.UnresolvedQuantity > 0m)
                    yield return ReconciliationAction.LinkStockCorrection;
                yield break;
        }

        var hasEnvelope = submission is not null && submission.Id == issue.OfflineSubmissionId;
        if (!hasEnvelope)
            yield break;

        // Held before posting (expired lease, recovery, another issuer): the envelope may be replayed or linked.
        if (ReplayableHoldCodes.Contains(issue.Code))
        {
            if (submission!.AttributableUserId is not null)
                yield return ReconciliationAction.RetryHistoricalLease;
            yield return ReconciliationAction.LinkExistingSale;
        }
        else if (SaleConflictCodes.Contains(issue.Code))
        {
            yield return ReconciliationAction.LinkExistingSale;
        }
    }
}
