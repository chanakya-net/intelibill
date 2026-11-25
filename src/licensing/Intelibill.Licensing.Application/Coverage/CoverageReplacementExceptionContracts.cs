using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

public static class CoverageReplacementExceptionErrors
{
    public static Error Invalid => Error.Validation("Coverage.ExceptionInvalid",
        "Check the shops, early start, paid cutover, disposition, reason and acknowledgements.");
    public static Error NotFound => Error.NotFound("Coverage.ExceptionNotFound",
        "The early coverage-replacement exception could not be found.");
    public static Error Changed => Error.Conflict("Coverage.ExceptionChanged",
        "The coverage, consents, permits or grants changed. Review the latest preview before confirming.");
    public static Error ConsentsRequired => Error.Conflict("Coverage.ExceptionConsentsRequired",
        "Current Billing Owner terms and both shop-owner consents are required before approval.");
    public static Error PayloadMismatch => Error.Conflict("Coverage.ExceptionPayloadMismatch",
        "This operation key was already used with different review details.");
    public static Error AccessDenied => Error.Forbidden("Coverage.ExceptionAccessDenied",
        "Only a Licensing Manager can review or approve early coverage-replacement exceptions.");
    public static Error RecentMfa => Error.Forbidden("Coverage.ExceptionRecentMfaRequired",
        "Sign in again with recent MFA before approving or recovering an early replacement exception.");
    public static Error Busy => Error.Failure("Coverage.ExceptionBusy",
        "Another coverage operation is in progress for this billing account. Try again.");
    public static Error Unavailable => Error.Failure("Coverage.ExceptionUnavailable",
        "Coverage exception storage is temporarily unavailable.");
}

public sealed record AdminCoverageExceptionSummaryV1(
    Guid ExceptionId, Guid AccountId, Guid TermId, long ProposalRevision,
    Guid OutgoingShopId, string OutgoingShopName, Guid IncomingShopId, string IncomingShopName,
    DateTimeOffset NormalRenewalAt, DateTimeOffset RequestedEarlyStartAt, DateTimeOffset PaidCutoverAt,
    string OverlapDisposition, DateTimeOffset? ObservedPermitDeadline, string State,
    string OutgoingConsentState, string IncomingConsentState,
    string OutgoingActivation, string IncomingActivation, string CourtesyActivation,
    Guid? CourtesyGrantId, DateTimeOffset CreatedAt);

public sealed record AdminCoverageExceptionListV1(
    Guid AccountId, DateTimeOffset EvaluatedAt, AdminCoverageExceptionSummaryV1[] Items);

public sealed record AdminCoverageExceptionConsentV1(
    string Leg, Guid ShopId, string ShopName, string Status,
    Guid? EventId, Guid? ActorId, long? AuthorityVersion, int? ActiveMembers, DateTimeOffset? RecordedAt);

public sealed record AdminCoverageExceptionApprovalV1(
    Guid ApprovalId, Guid ExceptionId, Guid AccountId, Guid TermId, long Generation,
    Guid OperationKey, Guid AdminId, string Disposition, string Reason,
    DateTimeOffset DecidedAt, string State, string? OutcomeReason,
    int? MerchantPermitCount, DateTimeOffset? MerchantMaxPermitEnd,
    Guid? CourtesyGrantId, Guid? CourtesyTermId, DateTimeOffset? CourtesyStartsAt, DateTimeOffset? CourtesyEndsAt,
    DateTimeOffset PaidCutoverAt, DateTimeOffset NormalRenewalAt,
    string OutgoingActivation, string IncomingActivation, string CourtesyActivation,
    DateTimeOffset? AppliedAt);

public sealed record AdminCoverageExceptionPreviewV1(
    Guid ExceptionId, Guid AccountId, Guid TermId, long ProposalRevision,
    string SourceHash, string TermsDigest, long GrantScopeVersion,
    Guid OutgoingShopId, string OutgoingShopName, Guid OutgoingAffiliationId,
    Guid IncomingShopId, string IncomingShopName, Guid IncomingAffiliationId,
    int Capacity, int MemberAllowance,
    DateTimeOffset NormalRenewalAt, DateTimeOffset RequestedEarlyStartAt, DateTimeOffset PaidCutoverAt,
    string OverlapDisposition,
    DateTimeOffset? ObservedPermitDeadline, DateTimeOffset ObservedAt,
    DateTimeOffset? LastContactAt, DateTimeOffset? LastSynchronizedAt, int ObservedPermitCount,
    bool DisconnectedQueuesUnknown, string DisconnectedQueuesNotice,
    Guid BillingOwnerUserId, int BillingOwnerRevision, bool BillingOwnerCurrent, bool BillingOwnerAcknowledged,
    AdminCoverageExceptionConsentV1 OutgoingConsent, AdminCoverageExceptionConsentV1 IncomingConsent,
    string RequestReason, string State, string? StateReason,
    bool CanApprove, string[] BlockingReasons,
    bool CourtesyGrantWillBeCreated, DateTimeOffset? CourtesyStartsAt, DateTimeOffset? CourtesyEndsAt,
    AdminCoverageExceptionApprovalV1? Approval);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveCoverageReplacementRequestV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("expectedProposalRevision")] long ExpectedProposalRevision,
    [property: JsonPropertyName("expectedSourceHash")] string ExpectedSourceHash,
    [property: JsonPropertyName("expectedTermsDigest")] string ExpectedTermsDigest,
    [property: JsonPropertyName("expectedGrantVersion")] long ExpectedGrantVersion,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("acknowledgedPermitsAndUnknownQueues")] bool AcknowledgedPermitsAndUnknownQueues,
    [property: JsonPropertyName("confirmed")] bool Confirmed);

public sealed record CoverageExceptionApprovalOutcome(AdminCoverageExceptionApprovalV1 Approval, bool Created);

public sealed record StoredExceptionApproval(
    Guid ApprovalId, Guid ExceptionId, Guid AdminId, string PayloadDigest, AdminCoverageExceptionApprovalV1 View);

public interface ICoverageReplacementExceptionStore
{
    Task<ErrorOr<CoverageReplacementExceptionPreviewV1>> ReadCustomerAsync(Guid account, Guid term, int page, CancellationToken ct);
    Task<ErrorOr<CoverageReplacementExceptionV1>> RequestAsync(Guid account, Guid term, Guid actor,
        RequestCoverageReplacementExceptionV1 command, CancellationToken ct);
    Task<ErrorOr<CoverageReplacementExceptionV1>> RecoverCustomerAsync(Guid account, Guid term, Guid actor,
        Guid key, CancellationToken ct);
    Task<ErrorOr<CoverageReplacementExceptionV1>> OwnerReadAsync(Guid account, Guid shop, Guid exceptionId, CancellationToken ct);

    Task<ErrorOr<T>> InAdminTransactionAsync<T>(Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct);
    Task<(AdminIdentity? Actor, AdminSession? Session)> LockProviderAsync(AdminSessionCaller caller, CancellationToken ct);
    Task EnterAdminAsync(Guid adminId, Guid sessionId, Guid account, CancellationToken ct);
    Task LockAccountAsync(Guid account, CancellationToken ct);
    Task<AdminCoverageExceptionListV1> ListAdminAsync(Guid account, DateTimeOffset now, CancellationToken ct);
    Task<ErrorOr<AdminCoverageExceptionPreviewV1>> PreviewAdminAsync(Guid account, Guid exceptionId, DateTimeOffset now, CancellationToken ct);
    Task<StoredExceptionApproval?> FindApprovalByKeyAsync(Guid account, Guid operationKey, DateTimeOffset now, CancellationToken ct);
    Task<ErrorOr<AdminCoverageExceptionApprovalV1>> RecordApprovalAndQueuePrepareAsync(
        Guid account, Guid exceptionId, AdminSessionCaller caller, DateTimeOffset mfaVerifiedAt, DateTimeOffset authorizedAt,
        DateTimeOffset decidedAt, ApproveCoverageReplacementRequestV1 request, string normalizedReason, string payloadDigest,
        AdminCoverageExceptionPreviewV1 preview, CancellationToken ct);
}
