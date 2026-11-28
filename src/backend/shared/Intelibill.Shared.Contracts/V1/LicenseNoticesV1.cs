using System.Text;
using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// Versioned contracts for role-appropriate licence notice planning, merchant in-app reads,
/// provider operator preview, billing contact consent, and signed merchant recipient verification.
/// Operational and commercial payloads use distinct DTOs so commercial fields never leak to shop staff.
/// </summary>
public static class LicenseNoticesV1
{
    public const int SchemaVersion = 1;
    public const string ShopRead = "licensing.shop-notices.read";
    public const string AccountRead = "licensing.account-notices.read";
    public const string ContactApprove = "licensing.notice-contacts.approve";
    public const string ContactList = "licensing.notice-contacts.list";
    public const string ContactOwnerRevoke = "licensing.notice-contacts.owner-revoke";
    public const string ContactSelfConsent = "licensing.notice-contacts.self-consent";
    public const string ContactSelfRevoke = "licensing.notice-contacts.self-revoke";

    public const string RecipientAuthorityPath = "/internal/merchant/v1/licensing/notice-recipients";
    public const string ProofHeader = "X-Licensing-Notice-Recipients-Proof";

    public const int PageSize = 50;
    public const int ContactPageSize = 25;
    public const int MaxShopNameSampleCount = 5;
    public const int MaxShopNamePreviewCodeUnits = 64;
    public const int MaxRecipientPageSize = 50;
    public const int MaxActorLookupCount = 50;
    public const int ShopResponseLimit = 128 * 1024;
    public const int AccountResponseLimit = 256 * 1024;
    public const int ContactResponseLimit = 32 * 1024;
    public const int RecipientAuthorityMaxRequestBytes = 8 * 1024;
    public const int RecipientAuthorityMaxResponseBytes = 128 * 1024;

    public const string ScopeAccount = "Account";
    public const string ScopeShop = "Shop";

    public const string AudienceCommercialBillingRole = "CommercialBillingRole";
    public const string AudienceCommercialContact = "CommercialContact";
    public const string AudienceShopOwnerOperational = "ShopOwnerOperational";
    public const string AudienceShopStaffOperational = "ShopStaffOperational";

    public const string PurposeTrialReminder = "TrialReminder";
    public const string PurposePaidRenewalReminder = "PaidRenewalReminder";
    public const string PurposeGraceNotice = "GraceNotice";

    public const string SourceTrial = "Trial";
    public const string SourcePaid = "Paid";
    public const string SourceComplimentary = "Complimentary";

    public const string ThresholdTrialMinus3Days = "TrialMinus3Days";
    public const string ThresholdTrialMinus1Day = "TrialMinus1Day";
    public const string ThresholdPaidMinus7Days = "PaidMinus7Days";
    public const string ThresholdPaidMinus1Day = "PaidMinus1Day";
    public const string ThresholdGraceStart = "GraceStart";
    public const string ThresholdGraceMinus1Day = "GraceMinus1Day";
    public const string ThresholdGraceExpiry = "GraceExpiry";

    public const string ChannelInApp = "InApp";
    public const string ChannelEmail = "Email";
    public const string StatusScheduled = "Scheduled";

    public const string GuidanceTrialEnding = "TrialEnding";
    public const string GuidanceTrialEndingPartialSuspension = "TrialEndingPartialSuspension";
    public const string GuidancePaidRenewalDue = "PaidRenewalDue";
    public const string GuidancePaidRenewalPartialSuspension = "PaidRenewalPartialSuspension";
    public const string GuidancePaidTermCancelled = "PaidTermCancelled";
    public const string GuidanceGraceActive = "GraceActive";
    public const string GuidanceGraceActivePartialSuspension = "GraceActivePartialSuspension";
    public const string GuidanceGraceEnding = "GraceEnding";
    public const string GuidanceGraceEndingPartialSuspension = "GraceEndingPartialSuspension";
    public const string GuidanceAccessExpired = "AccessExpired";
    public const string GuidanceSuspendedNoPaymentLift = "SuspendedNoPaymentLift";

    public static readonly string[] ScopeKinds = [ScopeAccount, ScopeShop];

    public static readonly string[] Audiences =
    [
        AudienceCommercialBillingRole,
        AudienceCommercialContact,
        AudienceShopOwnerOperational,
        AudienceShopStaffOperational,
    ];

    public static readonly string[] Purposes =
    [
        PurposeTrialReminder,
        PurposePaidRenewalReminder,
        PurposeGraceNotice,
    ];

    public static readonly string[] SourceKinds =
    [
        SourceTrial,
        SourcePaid,
        SourceComplimentary,
    ];

    public static readonly string[] Thresholds =
    [
        ThresholdTrialMinus3Days,
        ThresholdTrialMinus1Day,
        ThresholdPaidMinus7Days,
        ThresholdPaidMinus1Day,
        ThresholdGraceStart,
        ThresholdGraceMinus1Day,
        ThresholdGraceExpiry,
    ];

    public static readonly string[] SuppressionReasons =
        ["ScopeClosed", "SourceNoLongerCurrent", "WindowElapsed", "RestrictionChanged", "RecipientIneligible"];

    public static readonly string[] Channels = [ChannelInApp, ChannelEmail];
    public static readonly string[] Statuses = [StatusScheduled];

    public static readonly string[] GuidanceCodes =
    [
        GuidanceTrialEnding,
        GuidanceTrialEndingPartialSuspension,
        GuidancePaidRenewalDue,
        GuidancePaidRenewalPartialSuspension,
        GuidancePaidTermCancelled,
        GuidanceGraceActive,
        GuidanceGraceActivePartialSuspension,
        GuidanceGraceEnding,
        GuidanceGraceEndingPartialSuspension,
        GuidanceAccessExpired,
        GuidanceSuspendedNoPaymentLift,
    ];

    public static readonly string[] SupportedLocales =
    [
        "en-IN",
        "hi-IN",
        "ta-IN",
        "te-IN",
        "bn-IN",
        "ml-IN",
        "kn-IN",
        "mr-IN",
        "gu-IN",
    ];

    public static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return "en-IN";
        var trimmed = locale.Trim();
        return SupportedLocales.Contains(trimmed, StringComparer.Ordinal) ? trimmed : "en-IN";
    }

    /// <summary>
    /// Truncates a shop display name without splitting UTF-16 surrogate pairs so worst-case JSON
    /// escaping stays bounded under delegated merchant and provider page response limits.
    /// </summary>
    public static string BoundShopName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var trimmed = name.Trim();
        if (trimmed.Length <= MaxShopNamePreviewCodeUnits)
            return trimmed;

        var limit = MaxShopNamePreviewCodeUnits - 1;
        var sb = new StringBuilder(MaxShopNamePreviewCodeUnits);
        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (sb.Length + rune.Utf16SequenceLength > limit)
                break;
            sb.Append(rune.ToString());
        }

        sb.Append('…');
        return sb.ToString();
    }

    public static string[] BoundShopNameSample(IEnumerable<string> names) =>
    [
        .. names
            .Select(BoundShopName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Take(MaxShopNameSampleCount),
    ];

    public static string ShopNoticesPath(Guid shop, Guid? after = null) => after is { } id
        ? $"{MerchantBillingDelegationV1.InternalPrefix}/shops/{shop:D}/notices/after/{id:D}"
        : $"{MerchantBillingDelegationV1.InternalPrefix}/shops/{shop:D}/notices";

    public static string AccountNoticesPath(Guid account, Guid? after = null) => after is { } id
        ? $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/notices/after/{id:D}"
        : $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/notices";

    public static string AccountContactsPath(Guid account, Guid? after = null) => after is { } id
        ? $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/notice-contacts/after/{id:D}"
        : $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/notice-contacts";

    public static string ContactOwnerRevokePath(Guid account, Guid contactId) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/notice-contacts/{contactId:D}/revoke";

    public static string ContactSelfConsentPath(Guid contactId) =>
        $"{MerchantBillingDelegationV1.InternalPrefix}/notice-contacts/{contactId:D}/consent";

    public static string ContactSelfRevokePath(Guid contactId) =>
        $"{MerchantBillingDelegationV1.InternalPrefix}/notice-contacts/{contactId:D}/revoke";

    public static bool IsContactAction(string? action) => action is ContactApprove or ContactList
        or ContactOwnerRevoke or ContactSelfConsent or ContactSelfRevoke;
}

public sealed record NoticeRecipientAuthorityRequestV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("challenge")] Guid Challenge,
    [property: JsonPropertyName("deadline")] DateTimeOffset Deadline,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("affiliationRequestId")] Guid? AffiliationRequestId,
    [property: JsonPropertyName("shopId")] Guid? ShopId,
    [property: JsonPropertyName("actorIds")] Guid[] ActorIds,
    [property: JsonPropertyName("afterUserId")] Guid? AfterUserId,
    [property: JsonPropertyName("pageSize")] int PageSize)
{
    public bool IsValid() => SchemaVersion == LicenseNoticesV1.SchemaVersion
        && Challenge != Guid.Empty
        && AccountId != Guid.Empty
        && (ShopId is null || (ShopId != Guid.Empty && AffiliationRequestId is { } aff && aff != Guid.Empty))
        && (ShopId is not null || (AffiliationRequestId is null && AfterUserId is null))
        && (AfterUserId is null || AfterUserId != Guid.Empty)
        && PageSize is >= 1 and <= LicenseNoticesV1.MaxRecipientPageSize
        && ActorIds is { Length: <= LicenseNoticesV1.MaxActorLookupCount }
        && ActorIds.All(id => id != Guid.Empty)
        && (ShopId is null ? ActorIds.Length > 0 : ActorIds.Length == 0);
}

public sealed record NoticeRecipientActorV1(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("isLoginEnabled")] bool IsLoginEnabled,
    [property: JsonPropertyName("isEmailVerified")] bool IsEmailVerified,
    [property: JsonPropertyName("verifiedEmail")] string? VerifiedEmail,
    [property: JsonPropertyName("isPhoneVerified")] bool IsPhoneVerified,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("shopRole")] string? ShopRole,
    [property: JsonPropertyName("authorityVersion")] long? AuthorityVersion)
{
    public bool IsValidForCommercialActor() => IsValidBase()
        && (IsLoginEnabled && IsEmailVerified
            ? !string.IsNullOrWhiteSpace(VerifiedEmail)
            : VerifiedEmail is null);

    public bool IsValidForShopMember() => IsValidBase()
        && ShopRole is "Owner" or "Manager" or "Staff"
        && (string.Equals(ShopRole, "Owner", StringComparison.Ordinal)
            ? (IsLoginEnabled && IsEmailVerified ? !string.IsNullOrWhiteSpace(VerifiedEmail) : VerifiedEmail is null)
            : VerifiedEmail is null);

    private bool IsValidBase() => UserId != Guid.Empty
        && LicenseNoticesV1.SupportedLocales.Contains(Language, StringComparer.Ordinal)
        && (ShopRole is null or "Owner" or "Manager" or "Staff");
}

public sealed record NoticeRecipientAuthorityResponseV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("challenge")] Guid Challenge,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("shopId")] Guid? ShopId,
    [property: JsonPropertyName("affiliationRequestId")] Guid? AffiliationRequestId,
    [property: JsonPropertyName("affiliationMatches")] bool AffiliationMatches,
    [property: JsonPropertyName("observedAt")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("actors")] NoticeRecipientActorV1[] Actors,
    [property: JsonPropertyName("shopMembers")] NoticeRecipientActorV1[] ShopMembers,
    [property: JsonPropertyName("nextShopMemberCursor")] Guid? NextShopMemberCursor)
{
    public bool IsValid(NoticeRecipientAuthorityRequestV1 request) =>
        request.IsValid()
        && HasMatchingEnvelope(request)
        && HasValidRequestedActors(request)
        && HasValidShopMembersPage(request);

    private bool HasMatchingEnvelope(NoticeRecipientAuthorityRequestV1 request) =>
        SchemaVersion == LicenseNoticesV1.SchemaVersion
        && Challenge == request.Challenge
        && AccountId == request.AccountId
        && ShopId == request.ShopId
        && AffiliationRequestId == request.AffiliationRequestId
        && (request.ShopId is not null || AffiliationMatches);

    private bool HasValidRequestedActors(NoticeRecipientAuthorityRequestV1 request)
    {
        if (Actors is null || Actors.Length > request.ActorIds.Length || Actors.Length > LicenseNoticesV1.MaxActorLookupCount)
            return false;

        var requested = request.ActorIds.ToHashSet();
        var seen = new HashSet<Guid>();
        foreach (var actor in Actors)
        {
            if (!actor.IsValidForCommercialActor() || !requested.Contains(actor.UserId) || !seen.Add(actor.UserId))
                return false;
        }

        return true;
    }

    private bool HasValidShopMembersPage(NoticeRecipientAuthorityRequestV1 request)
    {
        if (ShopMembers is null)
            return false;
        if (request.ShopId is null || !AffiliationMatches)
            return ShopMembers.Length == 0 && NextShopMemberCursor is null;
        if (ShopMembers.Length > request.PageSize || ShopMembers.Length > LicenseNoticesV1.MaxRecipientPageSize)
            return false;

        var previous = request.AfterUserId;
        foreach (var member in ShopMembers)
        {
            if (!member.IsValidForShopMember())
                return false;
            if (previous is { } prev && member.UserId.CompareTo(prev) <= 0)
                return false;
            previous = member.UserId;
        }

        if (NextShopMemberCursor is not { } cursor)
            return true;

        return ShopMembers.Length == request.PageSize
            && cursor == ShopMembers[^1].UserId
            && (request.AfterUserId is not { } after || cursor.CompareTo(after) > 0);
    }
}
