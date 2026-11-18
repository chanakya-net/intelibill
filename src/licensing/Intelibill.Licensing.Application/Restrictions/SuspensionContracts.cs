using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Restrictions;

public sealed record SuspensionSelection(string? Scope, Guid? ShopId)
{
    [JsonIgnore]
    public bool IsValid => Scope == "Account" && ShopId is null || Scope == "Shop" && ShopId is { } id && id != Guid.Empty;
}

public sealed record SuspendAccessCommand(string? Scope, Guid? ShopId, string? Mode, DateTimeOffset? EffectiveAt,
    string? CustomerExplanation, string? InternalReason, string? ReviewReference, long? ExpectedVersion,
    string? ImpactDigest, Guid OperationKey);

public sealed record SuspensionShop(Guid ShopId, string ShopName, Guid AffiliationId, string? CoverageMode);
public sealed record SuspensionImpact(Guid AccountId, string AccountName, SuspensionSelection Selection,
    long Version, bool AlreadySuspended, IReadOnlyList<SuspensionShop> Shops, string ImpactDigest);

public sealed record SavedSuspension(Guid Id, Guid AccountId, Guid? ShopId, long Version, string Mode,
    DateTimeOffset EffectiveAt, string CustomerExplanation, string InternalReason, string ReviewReference,
    Guid ActorId, Guid SessionId, DateTimeOffset RecordedAt, Guid OperationKey, string PayloadDigest);

public sealed record SuspensionDelivery(Guid ShopId, string ShopName, long? AccountRevision, long? ShopRevision, string Status);

public sealed record SuspensionStatus(Guid Id, Guid AccountId, Guid? ShopId, long Version, string Mode,
    DateTimeOffset EffectiveAt, string CustomerExplanation, DateTimeOffset RecordedAt, string Propagation,
    IReadOnlyList<SuspensionDelivery> Shops, SuspensionReleaseStatus? Release = null);

public sealed record ReleaseSuspensionCommand(string? Reason, long? ExpectedVersion, string? ImpactDigest, Guid OperationKey);

public sealed record ReleaseShopImpact(Guid ShopId, string ShopName, Guid AffiliationId, string State, Guid? TermId,
    string? Basis, DateTimeOffset? StartsAt, DateTimeOffset? PaidThrough, DateTimeOffset? GraceEndsAt,
    DateTimeOffset? AccessEndsAt, int? MemberAllowance, ReleaseRestrictionImpact? SurvivingSuspension);

public sealed record ReleaseRestrictionImpact(string Mode, DateTimeOffset? EffectiveAt);

public sealed record SurvivingRestriction(Guid? Id, string Mode, DateTimeOffset? EffectiveAt, string CustomerExplanation);

public sealed record ReleaseImpact(Guid RestrictionId, long Version, DateTimeOffset EvaluatedAt,
    IReadOnlyList<ReleaseShopImpact> Shops, string ImpactDigest);

public sealed record SavedRelease(Guid Id, Guid RestrictionId, Guid AccountId, long Version,
    DateTimeOffset ReleasedAt, Guid ReleasedBy, Guid SessionId, DateTimeOffset MfaVerifiedAt, string Reason,
    Guid OperationKey, string PayloadDigest);

public sealed record SuspensionReleaseStatus(Guid Id, long Version, DateTimeOffset ReleasedAt, Guid ReleasedBy,
    string Propagation, IReadOnlyList<SuspensionDelivery> Shops);

public sealed record SuspensionOutcome(SuspensionStatus Status, bool Created);

public static class SuspensionErrors
{
    public static readonly Error Invalid = Error.Validation("Suspension.Invalid", "Select a scope and mode, and complete the suspension details.");
    public static readonly Error InvalidRelease = Error.Validation("Suspension.InvalidRelease", "Enter a reason and confirm the current release preview.");
    public static readonly Error Denied = Error.Forbidden("Suspension.AccessDenied");
    public static readonly Error RecentMfa = Error.Forbidden("Suspension.RecentMfaRequired", "Sign in again with MFA before changing a suspension.");
    public static readonly Error Missing = Error.NotFound("Suspension.NotFound");
    public static readonly Error Conflict = Error.Conflict("Suspension.VersionConflict", "The selected scope or its impact changed. Preview it again.");
    public static readonly Error AlreadySuspended = Error.Conflict("Suspension.AlreadySuspended", "This scope already has a saved suspension.");
    public static readonly Error PayloadMismatch = Error.Conflict("Suspension.PayloadMismatch");
}

internal static class SuspensionInput
{
    internal static bool Valid(SuspendAccessCommand command) =>
        new SuspensionSelection(command.Scope, command.ShopId).IsValid &&
        command.Mode is "Trading" or "Security" && command.ExpectedVersion is >= 0 &&
        EntitlementSnapshotsV1.IsDigest(command.ImpactDigest) && command.OperationKey != Guid.Empty &&
        (command.EffectiveAt is not { } at || EntitlementSnapshotsV1.IsInstant(at.ToUniversalTime())) &&
        Text(command.CustomerExplanation, 500) && Text(command.InternalReason, 1000) && Text(command.ReviewReference, 120);

    internal static bool Valid(ReleaseSuspensionCommand command) =>
        command.ExpectedVersion is > 0 && command.OperationKey != Guid.Empty &&
        EntitlementSnapshotsV1.IsDigest(command.ImpactDigest) && Text(command.Reason, 1000);

    private static bool Text(string? value, int limit) => value is { Length: > 0 } && value.Length <= limit &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(character => char.IsControl(character) && character != '\n');
}
