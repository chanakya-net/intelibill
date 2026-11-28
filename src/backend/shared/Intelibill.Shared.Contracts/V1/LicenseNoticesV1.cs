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
    public const string GuidancePaidRenewalDue = "PaidRenewalDue";
    public const string GuidancePaidTermCancelled = "PaidTermCancelled";
    public const string GuidanceGraceActive = "GraceActive";
    public const string GuidanceGraceEnding = "GraceEnding";
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

    public static readonly string[] Channels = [ChannelInApp, ChannelEmail];
    public static readonly string[] Statuses = [StatusScheduled];

    public static readonly string[] GuidanceCodes =
    [
        GuidanceTrialEnding,
        GuidancePaidRenewalDue,
        GuidancePaidTermCancelled,
        GuidanceGraceActive,
        GuidanceGraceEnding,
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

    public static string ShopNoticesPath(Guid shop, Guid? after = null) => after is { } id
        ? $"{MerchantBillingDelegationV1.InternalPrefix}/shops/{shop:D}/notices/after/{id:D}"
        : $"{MerchantBillingDelegationV1.InternalPrefix}/shops/{shop:D}/notices";

    public static string AccountNoticesPath(Guid account, Guid? after = null) => after is { } id
        ? $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/notices/after/{id:D}"
        : $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/notices";

    public static string AccountContactsPath(Guid account) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/notice-contacts";

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
        && (ShopId is not null || ActorIds.Length > 0);
}

public sealed record NoticeRecipientActorV1(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("isLoginEnabled")] bool IsLoginEnabled,
    [property: JsonPropertyName("isEmailVerified")] bool IsEmailVerified,
    [property: JsonPropertyName("verifiedEmail")] string? VerifiedEmail,
    [property: JsonPropertyName("isPhoneVerified")] bool IsPhoneVerified,
    [property: JsonPropertyName("verifiedPhone")] string? VerifiedPhone,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("shopRole")] string? ShopRole,
    [property: JsonPropertyName("authorityVersion")] long? AuthorityVersion)
{
    public bool IsValid() => UserId != Guid.Empty
        && LicenseNoticesV1.SupportedLocales.Contains(Language, StringComparer.Ordinal)
        && (!IsEmailVerified || !IsLoginEnabled || !string.IsNullOrWhiteSpace(VerifiedEmail))
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
    public bool IsValid(NoticeRecipientAuthorityRequestV1 request) => SchemaVersion == LicenseNoticesV1.SchemaVersion
        && Challenge == request.Challenge
        && AccountId == request.AccountId
        && ShopId == request.ShopId
        && AffiliationRequestId == request.AffiliationRequestId
        && Actors is { Length: <= LicenseNoticesV1.MaxActorLookupCount }
        && Actors.All(actor => actor.IsValid())
        && ShopMembers is { Length: <= LicenseNoticesV1.MaxRecipientPageSize }
        && ShopMembers.All(member => member.IsValid())
        && (NextShopMemberCursor is null || (ShopId is not null && NextShopMemberCursor != Guid.Empty && ShopMembers.Length > 0));
}

public sealed record ShopLicenseNoticeItemV1(
    [property: JsonPropertyName("noticeId")] Guid NoticeId,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("threshold")] string Threshold,
    [property: JsonPropertyName("accessState")] string AccessState,
    [property: JsonPropertyName("guidanceCode")] string GuidanceCode,
    [property: JsonPropertyName("templateKey")] string TemplateKey,
    [property: JsonPropertyName("dueAt")] DateTimeOffset DueAt,
    [property: JsonPropertyName("effectiveDeadlineAt")] DateTimeOffset EffectiveDeadlineAt,
    [property: JsonPropertyName("timeZone")] string TimeZone,
    [property: JsonPropertyName("locale")] string Locale,
    [property: JsonPropertyName("shopName")] string ShopName);

public sealed record ShopLicenseNoticesV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("shopId")] Guid ShopId,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt,
    [property: JsonPropertyName("notices")] ShopLicenseNoticeItemV1[] Notices,
    [property: JsonPropertyName("nextDueAt")] DateTimeOffset? NextDueAt,
    [property: JsonPropertyName("nextCursor")] Guid? NextCursor);

public sealed record AccountLicenseNoticeItemV1(
    [property: JsonPropertyName("noticeId")] Guid NoticeId,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("sourceKind")] string SourceKind,
    [property: JsonPropertyName("sourceId")] Guid SourceId,
    [property: JsonPropertyName("threshold")] string Threshold,
    [property: JsonPropertyName("accessState")] string AccessState,
    [property: JsonPropertyName("guidanceCode")] string GuidanceCode,
    [property: JsonPropertyName("templateKey")] string TemplateKey,
    [property: JsonPropertyName("dueAt")] DateTimeOffset DueAt,
    [property: JsonPropertyName("effectiveDeadlineAt")] DateTimeOffset EffectiveDeadlineAt,
    [property: JsonPropertyName("timeZone")] string TimeZone,
    [property: JsonPropertyName("locale")] string Locale,
    [property: JsonPropertyName("allowedChannels")] string[] AllowedChannels,
    [property: JsonPropertyName("coveredShopCount")] int CoveredShopCount,
    [property: JsonPropertyName("coveredShopNames")] string[] CoveredShopNames,
    [property: JsonPropertyName("evidencedTermPricePaise")] long? EvidencedTermPricePaise,
    [property: JsonPropertyName("evidencedTermCurrency")] string? EvidencedTermCurrency,
    [property: JsonPropertyName("renewalQuoteRequired")] bool RenewalQuoteRequired,
    [property: JsonPropertyName("paymentDoesNotLiftSuspension")] bool PaymentDoesNotLiftSuspension);

public sealed record AccountLicenseNoticesV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt,
    [property: JsonPropertyName("notices")] AccountLicenseNoticeItemV1[] Notices,
    [property: JsonPropertyName("nextDueAt")] DateTimeOffset? NextDueAt,
    [property: JsonPropertyName("nextCursor")] Guid? NextCursor);

public sealed record ProviderNotificationCommercialV1(
    [property: JsonPropertyName("evidencedTermPricePaise")] long? EvidencedTermPricePaise,
    [property: JsonPropertyName("evidencedTermCurrency")] string? EvidencedTermCurrency,
    [property: JsonPropertyName("renewalQuoteRequired")] bool RenewalQuoteRequired,
    [property: JsonPropertyName("destinationHint")] string? DestinationHint,
    [property: JsonPropertyName("recipientReference")] string? RecipientReference,
    [property: JsonPropertyName("coveredShopNames")] string[] CoveredShopNames);

public sealed record ProviderNotificationItemV1(
    [property: JsonPropertyName("jobId")] Guid JobId,
    [property: JsonPropertyName("scopeKind")] string ScopeKind,
    [property: JsonPropertyName("scopeId")] Guid ScopeId,
    [property: JsonPropertyName("audience")] string Audience,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("sourceKind")] string SourceKind,
    [property: JsonPropertyName("sourceId")] Guid SourceId,
    [property: JsonPropertyName("sourceRevision")] string SourceRevision,
    [property: JsonPropertyName("threshold")] string Threshold,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isDue")] bool IsDue,
    [property: JsonPropertyName("dueAt")] DateTimeOffset DueAt,
    [property: JsonPropertyName("effectiveDeadlineAt")] DateTimeOffset EffectiveDeadlineAt,
    [property: JsonPropertyName("timeZone")] string TimeZone,
    [property: JsonPropertyName("locale")] string Locale,
    [property: JsonPropertyName("allowedChannels")] string[] AllowedChannels,
    [property: JsonPropertyName("templateKey")] string TemplateKey,
    [property: JsonPropertyName("guidanceCode")] string GuidanceCode,
    [property: JsonPropertyName("coveredShopCount")] int CoveredShopCount,
    [property: JsonPropertyName("paymentDoesNotLiftSuspension")] bool PaymentDoesNotLiftSuspension,
    [property: JsonPropertyName("plannedAt")] DateTimeOffset PlannedAt,
    [property: JsonPropertyName("commercial")] ProviderNotificationCommercialV1? Commercial);

public sealed record ProviderNotificationsPageV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt,
    [property: JsonPropertyName("commercialIncluded")] bool CommercialIncluded,
    [property: JsonPropertyName("items")] ProviderNotificationItemV1[] Items,
    [property: JsonPropertyName("nextCursor")] Guid? NextCursor);

public sealed record ApproveBillingContactRequestV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("merchantUserId")] Guid? MerchantUserId,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("locale")] string? Locale);

public sealed record ConfirmBillingContactConsentRequestV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("verifiedEmail")] string? VerifiedEmail,
    [property: JsonPropertyName("proofToken")] string? ProofToken,
    [property: JsonPropertyName("optedIn")] bool OptedIn,
    [property: JsonPropertyName("locale")] string? Locale);

public sealed record RevokeBillingContactRequestV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey);

public sealed record BillingContactV1(
    [property: JsonPropertyName("contactId")] Guid ContactId,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("merchantUserId")] Guid? MerchantUserId,
    [property: JsonPropertyName("destinationHint")] string DestinationHint,
    [property: JsonPropertyName("locale")] string Locale,
    [property: JsonPropertyName("approvedAt")] DateTimeOffset ApprovedAt,
    [property: JsonPropertyName("verifiedAt")] DateTimeOffset? VerifiedAt,
    [property: JsonPropertyName("optedInAt")] DateTimeOffset? OptedInAt,
    [property: JsonPropertyName("revokedAt")] DateTimeOffset? RevokedAt,
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("eligible")] bool Eligible,
    [property: JsonPropertyName("verificationProofToken"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? VerificationProofToken = null);
