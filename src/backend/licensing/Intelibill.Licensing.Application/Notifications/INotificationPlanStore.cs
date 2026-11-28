using ErrorOr;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Notifications;

public static class NotificationErrors
{
    public static Error InvalidCursor => Error.Validation(
        "Notifications.InvalidCursor",
        "The requested notification cursor does not belong to this account.");

    public static Error ContactInvalid => Error.Validation(
        "Notifications.ContactInvalid",
        "Enter a valid billing contact email address and operation key.");

    public static Error ContactNotFound => Error.NotFound(
        "Notifications.ContactNotFound",
        "No matching billing contact was found.");

    public static Error ContactConflict => Error.Conflict(
        "Notifications.ContactConflict",
        "An active billing contact with that destination or identity already exists.");

    public static Error ContactProofInvalid => Error.Forbidden(
        "Notifications.ContactProofInvalid",
        "The billing contact verification proof is invalid, expired, or already used.");

    public static Error ContactOperationMismatch => Error.Conflict(
        "Notifications.ContactOperationMismatch",
        "This operation key was already used with different contact details.");

    public static Error Unavailable => Error.Failure(
        "Notifications.Unavailable",
        "Notification data is temporarily unavailable.");
}

public sealed record BillingMemberSnapshot(
    Guid MemberId,
    Guid MerchantUserId,
    BillingAccountRole Role,
    int Revision);

public sealed record BillingContactSnapshot(
    Guid ContactId,
    Guid? MerchantUserId,
    byte[] DestinationHash,
    string NormalizedEmail,
    string DestinationHint,
    string Locale,
    int Revision);

public sealed record ShopPlanSource(
    Guid ShopId,
    Guid AffiliationRequestId,
    string ShopName,
    bool IsClosed,
    bool IsSuspended,
    DateTimeOffset? HandoffEndsAt = null,
    DateTimeOffset? CoverageFrom = null);

public sealed record TermPlanSource(
    TermFactsRow Term,
    int TrialExtensionRevision,
    IReadOnlyList<ShopPlanSource> Shops);

public sealed record AccountPlanContext(
    Guid AccountId,
    bool IsAccountClosed,
    bool IsAccountSuspended,
    IReadOnlyList<BillingMemberSnapshot> ActiveMembers,
    IReadOnlyList<BillingContactSnapshot> EligibleContacts,
    IReadOnlyList<TermPlanSource> Terms);

public sealed record PlannedNotificationWrite(
    Guid AccountId,
    string ScopeKind,
    Guid ScopeId,
    string RecipientKey,
    Guid? RecipientUserId,
    Guid? ContactId,
    int? ExpectedMemberRevision,
    int? ExpectedContactRevision,
    Guid? ExpectedAffiliationRequestId,
    NoticeRestrictionScope ExpectedRestriction,
    string? ExpectedRosterFingerprint,
    string Audience,
    string Purpose,
    string SourceKind,
    Guid SourceId,
    string SourceRevision,
    string Threshold,
    DateTimeOffset DueAt,
    DateTimeOffset EffectiveDeadlineAt,
    string Locale,
    string[] AllowedChannels,
    string TemplateKey,
    string TemplateArgumentsJson,
    string? EmailDestination,
    byte[]? EmailDestinationHash,
    string? DestinationHint);

public sealed record NotificationPlanCommitResult(
    int Inserted,
    int SkippedDuplicate,
    int SkippedAuthorityChanged);

public sealed record NotificationPassResult(
    int AccountsScanned,
    int AccountsSkippedUnavailable,
    int Inserted,
    int SkippedDuplicate,
    int SkippedAuthorityChanged);

public sealed record NotificationBatchResult(
    NotificationPassResult Pass,
    Guid? NextAfterAccount);

public sealed record CurrentCommercialSource(
    Guid SourceId,
    string SourceRevision,
    NoticeRestrictionScope Restriction,
    int CoveredShopCount,
    int SuspendedShopCount,
    string[] CoveredShopNames,
    bool CoveredShopNamesTruncated);

public sealed record NotificationJobReadRow(
    Guid Id,
    Guid AccountId,
    string ScopeKind,
    Guid ScopeId,
    string RecipientKey,
    Guid? RecipientUserId,
    Guid? ContactId,
    string Audience,
    string Purpose,
    string SourceKind,
    Guid SourceId,
    string SourceRevision,
    string Threshold,
    DateTimeOffset DueAt,
    DateTimeOffset EffectiveDeadlineAt,
    string TimeZone,
    string Locale,
    string[] AllowedChannels,
    string Status,
    string TemplateKey,
    string TemplateArguments,
    string? DestinationHint,
    DateTimeOffset PlannedAt);

public sealed record NotificationReadPage(
    IReadOnlyList<NotificationJobReadRow> Rows,
    DateTimeOffset? NextDueAt,
    bool HasMore);

public interface INotificationPlanStore
{
    Task<IReadOnlyList<Guid>> DiscoverAccountsAsync(
        DateTimeOffset now,
        Guid? afterAccount,
        int take,
        CancellationToken ct);

    Task<AccountPlanContext?> ReadAccountPlanContextAsync(
        Guid accountId,
        DateTimeOffset now,
        int termPageSize,
        CancellationToken ct);

    Task<NotificationPlanCommitResult> CommitPlannedJobsAsync(
        Guid accountId,
        DateTimeOffset plannedAt,
        IReadOnlyList<PlannedNotificationWrite> writes,
        CancellationToken ct);
}

public interface IMerchantNoticeRecipientAuthority
{
    bool Enabled { get; }

    Task<NoticeRecipientAuthorityResponseV1?> InspectAsync(
        NoticeRecipientAuthorityRequestV1 request,
        CancellationToken ct);
}

public interface ILicenseNoticeReader
{
    Task<int> ReadTrialExtensionRevisionAsync(Guid accountId, Guid termId, CancellationToken ct);

    Task<IReadOnlyList<CurrentCommercialSource>> ReadCurrentAccountSourcesAsync(
        Guid accountId,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<NotificationReadPage> ReadCurrentDueShopNoticeAsync(
        Guid accountId,
        Guid shopId,
        Guid actorId,
        Guid sourceId,
        string sourceRevision,
        DateTimeOffset now,
        CancellationToken ct);

    Task<NotificationReadPage> ReadCurrentDueAccountNoticesAsync(
        Guid accountId,
        Guid actorId,
        IReadOnlyList<CurrentCommercialSource> activeSources,
        DateTimeOffset now,
        Guid? after,
        int take,
        CancellationToken ct);

    Task<ErrorOr<NotificationReadPage>> ReadProviderAccountJobsAsync(
        Guid accountId,
        Guid? after,
        int take,
        CancellationToken ct);
}

public interface IBillingContactStore
{
    Task<ErrorOr<ApprovedBillingContactResult>> ApproveAsync(
        Guid accountId,
        Guid ownerActorId,
        ApproveBillingContactRequestV1 request,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ErrorOr<BillingContactsPageV1>> ListAsync(
        Guid accountId,
        Guid? after,
        int take,
        CancellationToken ct);

    Task<ErrorOr<BillingContactV1>> RevokeByOwnerAsync(
        Guid accountId,
        Guid ownerActorId,
        Guid contactId,
        Guid operationKey,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ErrorOr<BillingContactV1>> ConfirmSelfConsentAsync(
        Guid actorId,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ErrorOr<BillingContactV1>> ConfirmStandaloneProofAsync(
        Guid accountId,
        Guid contactId,
        ConfirmBillingContactConsentRequestV1 request,
        DateTimeOffset now,
        CancellationToken ct);

    Task<ErrorOr<BillingContactV1>> RevokeByHolderAsync(
        Guid actorId,
        Guid contactId,
        Guid operationKey,
        DateTimeOffset now,
        CancellationToken ct);
}
