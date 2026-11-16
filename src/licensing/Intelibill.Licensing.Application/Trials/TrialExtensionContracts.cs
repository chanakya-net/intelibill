using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Licensing.Application.Grants;
using Intelibill.Licensing.Domain.Policies;

namespace Intelibill.Licensing.Application.Trials;

public sealed record TrialExtensionDraftV1(string? EndsAt, string? Reason, string? ReviewReference);

public sealed record ConfirmTrialExtensionV1(Guid OperationKey, string? EndsAt, string? Reason, string? ReviewReference,
    [property: JsonRequired] long CurrentVersion, string? SourceHash, bool Confirmed);

public sealed record TrialExtensionPreviewV1(Guid AccountId, Guid ShopId, string ShopName, Guid TermId,
    string StartsAt, string OriginalEndsAt, string CurrentEndsAt, string EndsAt, string Reason, string ReviewReference,
    long CurrentVersion, string SourceHash, DateTimeOffset EvaluatedAt, int AccountTrialCap, bool AccountTrialCapReached)
    : TrialExtensionResponseV1;

public sealed record TrialExtensionResultV1(Guid AccountId, Guid ShopId, string ShopName, Guid TermId,
    string StartsAt, string OriginalEndsAt, string PreviousEndsAt, string EndsAt, string Reason, string ReviewReference,
    long CurrentVersion, long ExtendedVersion, string SourceHash, DateTimeOffset RecordedAt,
    int AccountTrialCap, bool AccountTrialCapReached, Guid OperationKey, Guid ExtensionId) : TrialExtensionResponseV1;

public abstract record TrialExtensionResponseV1
{
    public int SchemaVersion { get; } = 1;
    public string Basis { get; } = "Trial";
    public string TimeZone { get; } = BillingCalendar.Zone;
    public bool PaymentRecorded { get; }
    public DateTimeOffset? GraceEndsAt { get; }
    public string PolicyVersion { get; } = TrialExtensionPolicy.Version;
}

public sealed record TrialExtensionOutcome(TrialExtensionResultV1 Result, bool Created);

internal sealed record TrialExtensionFacts(DateTimeOffset EndsAt, string Reason, string ReviewReference)
{
    internal static TrialExtensionFacts? Parse(string? end, string? reason, string? reference)
    {
        var trimmedReason = reason?.Trim();
        var trimmedReference = reference?.Trim();
        return ComplimentaryGrantFacts.ParseInstant(end) is { } instant && TrialExtensionPolicy.Supports(instant)
            && ValidText(trimmedReason, TrialExtensionPolicy.MaximumReasonLength)
            && ValidText(trimmedReference, TrialExtensionPolicy.MaximumReviewReferenceLength)
            ? new(instant, trimmedReason!, trimmedReference!) : null;
    }

    private static bool ValidText(string? value, int limit) =>
        value is { Length: > 0 } && value.Length <= limit && !value.Any(char.IsControl);

    internal static string Hash(params object[] values) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));

    internal static string Format(DateTimeOffset value) => ComplimentaryGrantFacts.Format(value);
}

public static class TrialExtensionErrors
{
    public static Error Invalid => Error.Validation("TrialExtension.Invalid",
        "Enter an exact end with an offset, a reason of 1 to 500 characters and a review reference of 1 to 80 characters. Confirm the preview to approve.");
    public static Error EndNotLater => Error.Validation("TrialExtension.EndNotLater", "The new end must be after the current end and in the future.");
    public static Error AccessDenied => Error.Forbidden("TrialExtension.AccessDenied", "Only a Licensing Manager can extend a trial.");
    public static Error RecentMfa => Error.Forbidden("TrialExtension.RecentMfaRequired", "Sign in again with MFA to continue.");
    public static Error TrialNotFound => Error.NotFound("TrialExtension.TrialNotFound", "No trial exists for this shop in the account.");
    public static Error OperationNotFound => Error.NotFound("TrialExtension.OperationNotFound", "The extension operation was not found.");
    public static Error NotEligible => Error.Conflict("TrialExtension.NotEligible", "This trial's coverage is ineligible for extension.");
    public static Error VersionConflict => Error.Conflict("TrialExtension.VersionConflict", "The trial or its account changed. Preview again.");
    public static Error PayloadMismatch => Error.Conflict("TrialExtension.PayloadMismatch", "This operation key belongs to a different request.");
    public static Error Busy => Error.Conflict("TrialExtension.Busy", "Another change to this account is in progress. Try again.");
    public static Error Unavailable => Error.Failure("TrialExtension.Unavailable", "Trial extensions are temporarily unavailable.");
}
