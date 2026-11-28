using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

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
    [property: JsonPropertyName("paymentDoesNotLiftSuspension")] bool PaymentDoesNotLiftSuspension,
    [property: JsonPropertyName("suspendedShopCount")] int SuspendedShopCount = 0,
    [property: JsonPropertyName("coveredShopNamesTruncated")] bool CoveredShopNamesTruncated = false);

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
    [property: JsonPropertyName("coveredShopNames")] string[] CoveredShopNames,
    [property: JsonPropertyName("coveredShopNamesTruncated")] bool CoveredShopNamesTruncated = false);

public sealed record ProviderEmailDeliveryV1(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("attempts")] int Attempts,
    [property: JsonPropertyName("lastAttemptAt")] DateTimeOffset? LastAttemptAt,
    [property: JsonPropertyName("nextAttemptAt")] DateTimeOffset? NextAttemptAt,
    [property: JsonPropertyName("outcomeAt")] DateTimeOffset? OutcomeAt,
    [property: JsonPropertyName("captureUrl")] string? CaptureUrl,
    [property: JsonPropertyName("suppressionReasonCode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SuppressionReasonCode = null);

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
    [property: JsonPropertyName("commercial")] ProviderNotificationCommercialV1? Commercial,
    [property: JsonPropertyName("suspendedShopCount")] int SuspendedShopCount = 0,
    [property: JsonPropertyName("emailDelivery")] ProviderEmailDeliveryV1? EmailDelivery = null);

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
    [property: JsonPropertyName("eligible")] bool Eligible);

public sealed record BillingContactsPageV1(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("items")] BillingContactV1[] Items,
    [property: JsonPropertyName("nextCursor")] Guid? NextCursor);
