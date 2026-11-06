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
/// Every unresolved case that names an invoice can be closed once that invoice has posted:
/// <c>LinkExistingSale</c> is offered on any case with an envelope (open or escalated, whatever its
/// reason) and on legacy held cases filed before envelopes existed. The action itself checks the
/// sale's identity (and, with an envelope, its payload hash), so offering it only records a fact.
/// </para>
/// <para>
/// There is deliberately no generic edit, renumber or delete: every resolution either links
/// existing evidence or replays the frozen envelope unchanged.
/// </para>
/// </remarks>
public static class ReconciliationActionPolicy
{
    /// <summary>The validator's informational warnings (an item renamed since the snapshot, say): the sale already posted.</summary>
    public const string ValidationVarianceCode = "offline_sync.validation_variance";

    /// <summary>
    /// Held before posting with a frozen envelope a reviewer may replay as it is: an expired lease, a
    /// recovery, another issuer, and every issue-time anomaly (clock drift, the fiscal-year rollover).
    /// The reviewer sees the claimed time, the first receipt and the lease window, and must give a reason.
    /// </summary>
    private static readonly HashSet<string> ReplayableHoldCodes = new(StringComparer.Ordinal)
    {
        Errors.OfflineReconciliation.HistoricalLeaseReviewRequired.Code,
        Errors.OfflineReconciliation.RecoveryReviewRequired.Code,
        Errors.OfflineReconciliation.IssuerNotSubmitter.Code,
        Errors.OfflineReconciliation.IssueTimeBeforeLease.Code,
        Errors.OfflineReconciliation.IssueTimeInFuture.Code,
        Errors.OfflineReconciliation.IssueFiscalYearMismatch.Code,
        Errors.OfflineReconciliation.IssuedAfterLeaseExpiry.Code,
        Errors.PostingPeriod.Closed(default).Code,
    };

    /// <summary>The types an invoice is held under before posting; legacy ones of these carry no envelope.</summary>
    private static readonly HashSet<ReconciliationIssueType> HeldTypes =
    [
        ReconciliationIssueType.InvoiceConflict,
        ReconciliationIssueType.ValidationConflict,
        ReconciliationIssueType.ArithmeticConflict,
        ReconciliationIssueType.IssueTimingReview,
        ReconciliationIssueType.ProvenanceReview,
        ReconciliationIssueType.RecoveryReview,
        ReconciliationIssueType.ClosedPeriodReview,
    ];

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

    /// <summary>
    /// True when the case may be closed by linking a posted sale: it has its envelope, unless that
    /// envelope's sale is already the case's own (a variance filed with the sale it describes); or it is
    /// a legacy held case with neither an envelope nor a sale.
    /// </summary>
    public static bool OffersSaleLink(ReconciliationIssue issue, OfflineSubmission? submission)
    {
        if (HasEnvelope(issue, submission))
            return submission!.SaleId is null || submission.SaleId != issue.SaleId;

        return IsLegacyHeld(issue);
    }

    /// <summary>A case held before envelopes existed: no envelope, no sale, a held type.</summary>
    public static bool IsLegacyHeld(ReconciliationIssue issue) =>
        issue.OfflineSubmissionId is null && issue.SaleId is null && HeldTypes.Contains(issue.IssueType);

    private static bool HasEnvelope(ReconciliationIssue issue, OfflineSubmission? submission) =>
        submission is not null && submission.Id == issue.OfflineSubmissionId;

    private static bool IsInformational(ReconciliationIssue issue) =>
        issue.IssueType is ReconciliationIssueType.PricingVariance
            or ReconciliationIssueType.DiscountVariance
            or ReconciliationIssueType.CustomerVariance
            or ReconciliationIssueType.DocumentReview
        // Keyed on the code: ValidationConflict is also the type of idempotency conflicts.
        || (issue.IssueType == ReconciliationIssueType.ValidationConflict
            && string.Equals(issue.Code, ValidationVarianceCode, StringComparison.Ordinal));

    private static IEnumerable<ReconciliationAction> ResolutionsFor(ReconciliationIssue issue, OfflineSubmission? submission)
    {
        if (IsInformational(issue))
            yield return ReconciliationAction.AcknowledgeVariance;

        if (issue.IssueType == ReconciliationIssueType.StockVariance
            && issue.ItemId is not null
            && issue.InventoryBatchId is not null
            && issue.UnresolvedQuantity > 0m)
        {
            yield return ReconciliationAction.LinkStockCorrection;
        }

        // Held before posting: the frozen envelope may be replayed, with a reason, under its verified issuer.
        if (HasEnvelope(issue, submission)
            && ReplayableHoldCodes.Contains(issue.Code)
            && submission!.AttributableUserId is not null)
        {
            yield return ReconciliationAction.RetryHistoricalLease;
        }

        if (OffersSaleLink(issue, submission))
            yield return ReconciliationAction.LinkExistingSale;
    }
}
