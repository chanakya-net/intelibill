using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Restrictions;

public sealed record ReviewReopeningCommand(string? Reason, bool OwnerConsentConfirmed, string? OwnerConsentReference,
    Guid OwnerConsentUserId, DateTimeOffset OwnerConsentAt, string? OwnerConsentEvidence,
    long ExpectedVersion, string? ImpactDigest, Guid OperationKey)
{
    internal bool IsValid() => ExpectedVersion > 0 && OperationKey != Guid.Empty &&
        EntitlementSnapshotsV1.IsDigest(ImpactDigest) && Text(Reason, 1000) && OwnerConsentConfirmed &&
        Text(OwnerConsentReference, 120) && OwnerConsentUserId != Guid.Empty &&
        EntitlementSnapshotsV1.IsInstant(OwnerConsentAt) && Text(OwnerConsentEvidence, 2000);

    private static bool Text(string? value, int max) => value is { Length: > 0 } && value.Length <= max
        && !string.IsNullOrWhiteSpace(value) && !value.Any(character => char.IsControl(character) && character != '\n');
}

public sealed record ReopeningImpact(Guid ClosureId, string ClosureScope, DateTimeOffset ClosedAt, Guid ShopId,
    string ShopName, Guid AffiliationRequestId, long Version, DateTimeOffset EvaluatedAt, SubscriptionTermStatusV1? Coverage,
    bool CoverageAdequate, long OtherActiveClosures, ReleaseRestrictionImpact? SurvivingSuspension, string ImpactDigest);

public sealed record ReopeningTarget(Guid ClosureId, Guid ShopId, Guid AffiliationRequestId, string ShopName,
    string ClosureScope, DateTimeOffset ClosedAt, long Version, bool Reopened, long OtherActiveClosures);

public sealed record ShopClosureStatus(DateTimeOffset? ClosedAt, DateTimeOffset? ReopenedAt, long? ReopeningRevision);

public sealed record CheckoutClosureStatus(Guid ShopId, bool Closed, long? ReopeningRevision, bool ReopeningAcknowledged);

public sealed record ReopeningDecision(Guid Id, Guid ClosureId, Guid ShopId, long Version, DateTimeOffset ReopenedAt);

public sealed record ReopeningOutcome(ReopeningDecision Decision, bool Created);

public sealed record ReopeningOperation(Guid Id, Guid ClosureId, Guid ShopId, long Version,
    DateTimeOffset ReopenedAt, Guid ReopenedBy, string PayloadDigest)
{
    internal ReopeningDecision Decision => new(Id, ClosureId, ShopId, Version, ReopenedAt);
}

public sealed record NewReopening(ReopeningDecision Decision, Guid AccountId, Guid AffiliationRequestId,
    Guid AdminId, Guid SessionId, DateTimeOffset MfaAt, DateTimeOffset EvaluatedAt, string PayloadDigest, ReviewReopeningCommand Command,
    SubscriptionTermStatusV1 Coverage);

internal static class ReopeningErrors
{
    internal static Error Invalid => Error.Validation("Reopening.Invalid", "Record the review reason and explicit consent from the current shop owner.");
    internal static Error Denied => Error.Forbidden("Reopening.AccessDenied", "Licensing Manager access is required to review reopening.");
    internal static Error RecentMfa => Error.Forbidden("Reopening.RecentMfaRequired", "Verify MFA again before saving the reopening.");
    internal static Error Missing => Error.NotFound("Reopening.NotFound", "This closure does not apply to the shop in this billing account.");
    internal static Error Conflict => Error.Conflict("Reopening.VersionConflict", "Shop coverage or restrictions changed. Review the current details.");
    internal static Error Coverage => Error.Conflict("Reopening.CoverageRequired", "The shop requires active coverage other than a trial.");
    internal static Error Reopened => Error.Conflict("Reopening.AlreadyReopened", "This closure has already been reviewed and reopened.");
    internal static Error PayloadMismatch => Error.Conflict("Reopening.PayloadMismatch", "This operation key belongs to a different reopening decision.");
    internal static Error Consent => Error.Conflict("Reopening.ConsentRequired", "Record new owner consent for this closure, with its own review reference.");
}
