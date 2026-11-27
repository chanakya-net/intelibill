using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ErrorOr;
using Intelibill.Licensing.Domain.Policies;

namespace Intelibill.Licensing.Application.Grants;

/// <summary>
/// Provider request to preview a dated complimentary grant. Dates are ISO 8601 strings with an explicit offset
/// (a seventh fractional digit must be zero: PostgreSQL keeps microseconds) so an ambiguous local time can never become a stored instant.
/// </summary>
public sealed record ComplimentaryGrantDraftV1(
    [property: JsonPropertyName("shopIds")] Guid[]? ShopIds,
    [property: JsonPropertyName("startsAt")] string? StartsAt,
    [property: JsonPropertyName("endsAt")] string? EndsAt,
    [property: JsonPropertyName("memberAllowance")] int MemberAllowance,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("paymentOrderId")] Guid? PaymentOrderId = null);

/// <summary>Confirmation of an exact preview: the same facts plus the preview's revision and source binding.</summary>
public sealed record ConfirmComplimentaryGrantV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("shopIds")] Guid[]? ShopIds,
    [property: JsonPropertyName("startsAt")] string? StartsAt,
    [property: JsonPropertyName("endsAt")] string? EndsAt,
    [property: JsonPropertyName("memberAllowance")] int MemberAllowance,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("expectedVersion")] long ExpectedVersion,
    [property: JsonPropertyName("sourceHash")] string? SourceHash,
    [property: JsonPropertyName("confirmed")] bool Confirmed,
    [property: JsonPropertyName("paymentOrderId")] Guid? PaymentOrderId = null);

public sealed record ComplimentaryGrantShopV1(
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("shopName")] string ShopName,
    [property: JsonPropertyName("baselineState")] string BaselineState,
    [property: JsonPropertyName("baselineBasis")] string? BaselineBasis,
    [property: JsonPropertyName("baselineAccessEndsAt")] DateTimeOffset? BaselineAccessEndsAt);

public sealed record ComplimentaryGrantPreviewV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("basis")] string Basis,
    [property: JsonPropertyName("shopIds")] Guid[] ShopIds,
    [property: JsonPropertyName("shops")] ComplimentaryGrantShopV1[] Shops,
    [property: JsonPropertyName("startsAt")] string StartsAt,
    [property: JsonPropertyName("endsAt")] string EndsAt,
    [property: JsonPropertyName("timeZone")] string TimeZone,
    [property: JsonPropertyName("coverageMode")] string CoverageMode,
    [property: JsonPropertyName("shopCapacity")] int ShopCapacity,
    [property: JsonPropertyName("memberAllowance")] int MemberAllowance,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("expectedVersion")] long ExpectedVersion,
    [property: JsonPropertyName("sourceHash")] string SourceHash,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt,
    [property: JsonPropertyName("paymentRecorded")] bool PaymentRecorded,
    [property: JsonPropertyName("graceEndsAt")] DateTimeOffset? GraceEndsAt,
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("paymentOrderId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? PaymentOrderId = null);

public sealed record ComplimentaryGrantResultV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("grantId")] Guid GrantId,
    [property: JsonPropertyName("termId")] Guid TermId,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("basis")] string Basis,
    [property: JsonPropertyName("shopIds")] Guid[] ShopIds,
    [property: JsonPropertyName("startsAt")] string StartsAt,
    [property: JsonPropertyName("endsAt")] string EndsAt,
    [property: JsonPropertyName("timeZone")] string TimeZone,
    [property: JsonPropertyName("coverageMode")] string CoverageMode,
    [property: JsonPropertyName("shopCapacity")] int ShopCapacity,
    [property: JsonPropertyName("memberAllowance")] int MemberAllowance,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("expectedVersion")] long ExpectedVersion,
    [property: JsonPropertyName("grantedVersion")] long GrantedVersion,
    [property: JsonPropertyName("sourceHash")] string SourceHash,
    [property: JsonPropertyName("recordedAt")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("paymentRecorded")] bool PaymentRecorded,
    [property: JsonPropertyName("graceEndsAt")] DateTimeOffset? GraceEndsAt,
    [property: JsonPropertyName("policyVersion")] string PolicyVersion,
    [property: JsonPropertyName("paymentOrderId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? PaymentOrderId = null);

/// <summary>An outcome and whether it was newly written (201) or replayed (200).</summary>
public sealed record ComplimentaryGrantOutcome(ComplimentaryGrantResultV1 Result, bool Created);

/// <summary>Canonical, validated grant facts: sorted distinct shops, UTC microsecond instants, trimmed reason.</summary>
public sealed record ComplimentaryGrantFacts(Guid[] ShopIds, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    int MemberAllowance, string Reason, Guid? PaymentOrderId = null)
{
    public const int SchemaVersion = 1;
    public const string Basis = "Complimentary";
    public const string TimeZone = "Asia/Kolkata";
    private static readonly Regex Instant = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public string CoverageMode => ComplimentaryAccessPolicy.CoverageMode(ShopIds.Length);

    public static ComplimentaryGrantFacts? From(Guid[]? shops, string? startsAt, string? endsAt, int memberAllowance, string? reason, Guid? paymentOrderId = null)
    {
        var trimmed = reason?.Trim();
        if (paymentOrderId == Guid.Empty || shops is not { Length: > 0 and <= ComplimentaryAccessPolicy.MaximumShops } || shops.Contains(Guid.Empty)
            || shops.Distinct().Count() != shops.Length || ParseInstant(startsAt) is not { } start
            || ParseInstant(endsAt) is not { } end || !ComplimentaryAccessPolicy.ValidInterval(start, end)
            || !ComplimentaryAccessPolicy.ValidCapacity(shops.Length, memberAllowance)
            || trimmed is not { Length: > 0 and <= ComplimentaryAccessPolicy.MaximumReasonLength } || trimmed.Any(char.IsControl))
            return null;
        return new([.. shops.Order()], start, end, memberAllowance, trimmed, paymentOrderId);
    }

    public static DateTimeOffset? ParseInstant(string? value) =>
        value is { Length: <= 40 } && Instant.IsMatch(value)
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime() : null;

    /// <summary>Round-trip UTC text with exactly six fractional digits, the same everywhere it is shown.</summary>
    public static string Format(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
}

public static class ComplimentaryGrantErrors
{
    public static Error Invalid => Error.Validation("Grant.Invalid",
        "Name 1 to 100 distinct shops, an explicit start before an explicit end, a member allowance of 1 to 100000, a reason of 1 to 500 characters and confirm the preview.");
    public static Error AccessDenied => Error.Forbidden("Grant.AccessDenied",
        "Only a Licensing Manager can grant complimentary access.");
    public static Error RecentMfa => Error.Forbidden("Grant.RecentMfaRequired", "Sign in again with MFA to continue.");
    public static Error ShopNotAffiliated => Error.Validation("Grant.ShopNotAffiliated",
        "Every named shop must already belong to this billing account.");
    public static Error InvalidPaymentOrder => Error.Validation("Grant.InvalidPaymentOrder",
        "Choose a fulfilled order in this account covering all named shops.");
    public static Error AlreadyEnded => Error.Validation("Grant.AlreadyEnded", "A new grant must end in the future.");
    public static Error Overlap => Error.Conflict("Grant.Overlap",
        "A named shop already has complimentary access in part of this window.");
    public static Error VersionConflict => Error.Conflict("Grant.VersionConflict",
        "The account's grants or named shops changed since the preview. Preview again.");
    public static Error PayloadMismatch => Error.Conflict("Grant.PayloadMismatch",
        "This operation key already belongs to a different grant request.");
    public static Error Missing => Error.NotFound("Grant.OperationNotFound", "The grant operation was not found.");
    public static Error Busy => Error.Conflict("Grant.Busy", "Another change to this account is in progress. Try again.");
    public static Error Unavailable => Error.Failure("Grant.Unavailable", "Complimentary grants are temporarily unavailable.");
}
