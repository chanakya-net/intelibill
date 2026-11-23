using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation;

/// <summary>
/// What evidence a case may be linked to. The resolve command and the console's link candidates use
/// these same checks, so the console never offers something the action would refuse.
/// </summary>
internal static class ReconciliationLinkRules
{
    /// <summary>
    /// Procurement evidence for a stock shortage: an increase on the same item and batch, for found
    /// stock or another gain (a stock count alone is not evidence), not voided, covering the shortage.
    /// Whether another case already links it is checked separately.
    /// </summary>
    public static bool CoversShortage(InventoryAdjustment adjustment, Guid shopId, Guid itemId, Guid batchId, decimal unresolved) =>
        adjustment.ShopId == shopId
        && adjustment.ItemId == itemId
        && adjustment.InventoryBatchId == batchId
        && adjustment.Direction == InventoryAdjustmentDirection.Increase
        && adjustment.Reason is InventoryAdjustmentReason.FoundStock or InventoryAdjustmentReason.OtherGain
        && !adjustment.IsVoided
        && adjustment.Quantity >= unresolved;

    /// <summary>
    /// Null when the case may be linked to <paramref name="sale"/> (already read within the case's shop);
    /// otherwise why not.
    /// </summary>
    /// <remarks>
    /// With an envelope: the sale carries the envelope's device and client sale id, and either its exact
    /// payload hash (and the envelope is unlinked or already linked to this sale), or — for an
    /// idempotency conflict — it is the case's own sale and matches an earlier envelope, the conflicting
    /// one staying unlinked as evidence. Without an envelope (a legacy held case): the device and client
    /// sale id alone, as nothing else was stored.
    /// </remarks>
    public static async Task<Error?> CheckSaleAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        Sale sale,
        IOfflineSubmissionRepository submissionRepository,
        CancellationToken cancellationToken)
    {
        if (submission is null)
        {
            if (!ReconciliationActionPolicy.IsLegacyHeld(issue))
                return Errors.Reconciliation.EnvelopeMissing;

            return SameIdentity(sale, issue.DeviceId, issue.ClientSaleId) ? null : Errors.Reconciliation.SaleIdentityMismatch;
        }

        if (!SameIdentity(sale, submission.DeviceId, submission.ClientSaleId))
            return Errors.Reconciliation.SaleIdentityMismatch;

        if (IsIdempotencyConflict(issue))
        {
            if (issue.SaleId != sale.Id)
                return Errors.Reconciliation.SaleIdentityMismatch;

            var original = await submissionRepository.FindEarliestByHashAsync(
                issue.ShopId,
                submission.DeviceId,
                submission.ClientSaleId,
                sale.RequestHash,
                cancellationToken);
            return original is null || original.Id == submission.Id ? Errors.Reconciliation.SaleIdentityMismatch : null;
        }

        if (!string.Equals(sale.RequestHash, submission.PayloadHash, StringComparison.Ordinal))
            return Errors.Reconciliation.SaleIdentityMismatch;

        return submission.SaleId is null || submission.SaleId == sale.Id ? null : Errors.Reconciliation.SaleIdentityMismatch;
    }

    /// <summary>
    /// Strict exact-transaction linkage check for licence history review cases: requires a retained
    /// envelope, offline posted sale in the same shop, matching device/client sale id, recomputed and
    /// posted payload hash, invoice number, original soldAt (PostgreSQL microsecond precision),
    /// consistent recorded issuer, and compatible envelope link.
    /// </summary>
    public static async Task<Error?> CheckHistorySaleAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        Sale sale,
        IOfflineSubmissionRepository submissionRepository,
        CancellationToken cancellationToken)
    {
        if (submission is null || submission.ShopId != issue.ShopId || submission.Id != issue.OfflineSubmissionId)
            return Errors.Reconciliation.EnvelopeMissing;

        if (sale.ShopId != issue.ShopId || sale.Source != SaleSource.Offline)
            return Errors.Reconciliation.SaleIdentityMismatch;

        if (!SameIdentity(sale, submission.DeviceId, submission.ClientSaleId))
            return Errors.Reconciliation.SaleIdentityMismatch;

        OfflineSaleSyncCommand? payload;
        try { payload = OfflineSubmissionPayload.Deserialize(submission.PayloadJson); }
        catch (JsonException) { return Errors.Reconciliation.SaleIdentityMismatch; }
        if (payload is null)
            return Errors.Reconciliation.SaleIdentityMismatch;

        var recomputedHash = OfflineSaleSyncIdempotencyHasher.ComputeHash(issue.ShopId, submission.DeviceId, payload);
        if (!string.Equals(recomputedHash, submission.PayloadHash, StringComparison.Ordinal)
            || !string.Equals(sale.RequestHash, submission.PayloadHash, StringComparison.Ordinal))
            return Errors.Reconciliation.SaleIdentityMismatch;

        if (!string.Equals(sale.InvoiceNumber, submission.InvoiceNumber, StringComparison.Ordinal)
            || !string.Equals(payload.InvoiceNumber.Trim(), submission.InvoiceNumber, StringComparison.Ordinal))
            return Errors.Reconciliation.SaleIdentityMismatch;

        var expectedSoldAt = OfflinePermitContractV1.Instant(submission.ClaimedSoldAt);
        if (OfflinePermitContractV1.Instant(sale.SoldAt) != expectedSoldAt
            || OfflinePermitContractV1.Instant(payload.SoldAt) != expectedSoldAt)
            return Errors.Reconciliation.SaleIdentityMismatch;

        if (!MatchesRecordedIssuer(sale, submission, payload))
            return Errors.Reconciliation.SaleIdentityMismatch;

        if (submission.SaleId is not null && submission.SaleId != sale.Id)
            return Errors.Reconciliation.SaleIdentityMismatch;

        return await CheckSaleAsync(issue, submission, sale, submissionRepository, cancellationToken);
    }

    private static bool MatchesRecordedIssuer(Sale sale, OfflineSubmission submission, OfflineSaleSyncCommand payload)
    {
        if (sale.ActorUserId == Guid.Empty)
            return false;
        if (submission.OriginalIssuerUserId is { } original && sale.ActorUserId != original)
            return false;
        if (submission.AttributableUserId is { } attributable && sale.ActorUserId != attributable)
            return false;
        if (submission.AssertedIssuerUserId is { } asserted && sale.ActorUserId != asserted)
            return false;
        if (payload.IssuedByUserId is { } claimed && sale.ActorUserId != claimed)
            return false;
        return true;
    }

    /// <summary>The conflicting envelope of an idempotency conflict is evidence and is never linked.</summary>
    public static bool IsIdempotencyConflict(ReconciliationIssue issue) =>
        string.Equals(issue.Code, Errors.Sale.IdempotencyConflict.Code, StringComparison.Ordinal);

    private static bool SameIdentity(Sale sale, string deviceId, string clientSaleId) =>
        string.Equals(sale.DeviceId, deviceId, StringComparison.Ordinal)
        && string.Equals(sale.ClientSaleId, clientSaleId, StringComparison.Ordinal);
}
