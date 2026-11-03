using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    /// <summary>
    /// Reasons an offline invoice is held for review instead of posting. Each is filed as the code of
    /// one reconciliation case and answered in the sync or recovery result.
    /// </summary>
    public static class OfflineReconciliation
    {
        public static Error HistoricalLeaseReviewRequired =>
            Error.Validation("OfflineReconciliation.HistoricalLeaseReviewRequired", "The invoice reached the server only after its invoice range expired, so an owner or manager must review it before it posts.");

        public static Error IssuedAfterLeaseExpiry =>
            Error.Validation("OfflineReconciliation.IssuedAfterLeaseExpiry", "The invoice claims an issue time after its invoice range expired.");

        public static Error IssueTimeBeforeLease =>
            Error.Validation("OfflineReconciliation.IssueTimeBeforeLease", "The invoice claims an issue time before its invoice range was reserved.");

        public static Error IssueTimeInFuture =>
            Error.Validation("OfflineReconciliation.IssueTimeInFuture", "The invoice claims an issue time later than the server received it.");

        public static Error IssueFiscalYearMismatch =>
            Error.Validation("OfflineReconciliation.IssueFiscalYearMismatch", "The invoice's issue time falls outside the fiscal year of its invoice range.");

        public static Error IssuerNotSubmitter =>
            Error.Validation("OfflineReconciliation.IssuerNotSubmitter", "The invoice was issued by a different user than the one sending it.");

        public static Error RecoveryReviewRequired =>
            Error.Validation("OfflineReconciliation.RecoveryReviewRequired", "A recovered invoice must be reviewed by an owner or manager before it posts.");

        public static Error ProvenanceUnknown =>
            Error.Validation("OfflineReconciliation.ProvenanceUnknown", "The invoice does not record who issued it.");

        public static Error IssuerUnverified =>
            Error.Validation("OfflineReconciliation.IssuerUnverified", "The recorded issuer has no history on this device.");
    }

    /// <summary>Errors from reading and resolving reconciliation cases.</summary>
    public static class Reconciliation
    {
        public static Error CaseNotFound =>
            Error.NotFound("Reconciliation.CaseNotFound", "The reconciliation case was not found.");

        public static Error CaseVersionConflict =>
            Error.Conflict("Reconciliation.CaseVersionConflict", "The case changed since it was read. Reload it and try again.");

        public static Error CaseAlreadyResolved =>
            Error.Conflict("Reconciliation.CaseAlreadyResolved", "The case is already resolved.");

        public static Error ActionNotAllowed =>
            Error.Validation("Reconciliation.ActionNotAllowed", "This action is not available for the case.");

        public static Error ActionForbidden =>
            Error.Forbidden("Reconciliation.ActionForbidden", "You are not allowed to take this action on the case.");

        public static Error UnknownAction =>
            Error.Validation("Reconciliation.UnknownAction", "The action is not recognised.");

        public static Error ReasonRequired =>
            Error.Validation("Reconciliation.ReasonRequired", "A reason is required.");

        public static Error ReasonInvalid =>
            Error.Validation("Reconciliation.ReasonInvalid", "The reason contains a character that cannot be stored.");

        public static Error InvalidStatusFilter =>
            Error.Validation("Reconciliation.InvalidStatusFilter", "The status filter is not recognised.");

        public static Error LinkedOperationRequired =>
            Error.Validation("Reconciliation.LinkedOperationRequired", "This action needs the operation it links to.");

        public static Error LinkedAdjustmentInvalid =>
            Error.Validation("Reconciliation.LinkedAdjustmentInvalid", "The linked stock adjustment does not cover the unresolved quantity for this item and batch.");

        public static Error OperationAlreadyLinked =>
            Error.Conflict("Reconciliation.OperationAlreadyLinked", "The operation is already linked to another case.");

        public static Error SaleIdentityMismatch =>
            Error.Validation("Reconciliation.SaleIdentityMismatch", "The sale does not match the device, client sale id and payload of the case.");

        public static Error ProvenanceUnverified =>
            Error.Conflict("Reconciliation.ProvenanceUnverified", "Nobody can be verified as the issuer of this invoice, so it cannot be posted.");

        public static Error EnvelopeMissing =>
            Error.Conflict("Reconciliation.EnvelopeMissing", "The case has no stored invoice to act on.");

        public static Error ReplayRejected =>
            Error.Conflict("Reconciliation.ReplayRejected", "The stored invoice could not be posted.");

        public static Error ConcurrentChange =>
            Error.Conflict("Reconciliation.ConcurrentChange", "Another change was committed at the same time. Reload and try again.");
    }

    /// <summary>Errors from owner recovery bundles; the last two answer one invoice row.</summary>
    public static class Recovery
    {
        public static Error BundleInvalid =>
            Error.Validation("Recovery.BundleInvalid", "The recovery bundle could not be read.");

        public static Error BundleFormatUnsupported =>
            Error.Validation("Recovery.BundleFormatUnsupported", "The recovery bundle format or version is not supported.");

        public static Error BundleChecksumMismatch =>
            Error.Validation("Recovery.BundleChecksumMismatch", "The recovery bundle's invoices do not match its checksum.");

        public static Error BundleShopMismatch =>
            Error.Validation("Recovery.BundleShopMismatch", "The recovery bundle belongs to another shop.");

        public static Error BundleEmpty =>
            Error.Validation("Recovery.BundleEmpty", "The recovery bundle contains no invoices.");

        public static Error BundleTooLarge =>
            Error.Validation("Recovery.BundleTooLarge", "The recovery bundle contains too many invoices.");

        public static Error LeaseNotOwned =>
            Error.Validation("Recovery.LeaseNotOwned", "The invoice number was not reserved for the bundle's device.");

        public static Error IssuerMismatch =>
            Error.Validation("Recovery.IssuerMismatch", "The recorded issuer has never been a member of this shop.");

        public static Error NotAuthorized =>
            Error.Forbidden("Recovery.NotAuthorized", "Only an active owner or manager can submit a recovery bundle.");
    }
}
