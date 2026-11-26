using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

public sealed record TransferSchedule(Guid TermId, DateTimeOffset StartsAt, DateTimeOffset? PaidThroughAt,
    DateTimeOffset? AccessEndsAt);
public sealed record TransferSummary(Guid TransferId, Guid ShopId, string ShopName, Guid DestinationAccountId);
public sealed record TransferReview(Guid TransferId, long ProposalRevision, string TermsDigest,
    Guid SourceAccountId, string SourceAccountName, Guid DestinationAccountId, string? DestinationAccountName,
    Guid ShopId, string ShopName, Guid SourceAffiliationId, Guid SourceTermId, string Treatment,
    DateTimeOffset CutoverAt, DateTimeOffset SourcePaidThroughAt, DateTimeOffset ExpiresAt, int? MemberAllowance,
    TransferSchedule[] SourceTerms, TransferSchedule[] DestinationTerms, CoverageTransferConsentStatusV1[] Consents,
    int ObservedPermitCount, DateTimeOffset? ObservedPermitDeadline, bool DisconnectedQueuesUnknown, string? Blocker,
    string ReviewDigest, string? ApprovalState);

public interface ICoverageTransferApprovalStore
{
    Task<StoredTransferApproval?> LookupAsync(Guid source, Guid key, CancellationToken ct);
    Task<ErrorOr<StoredTransferApproval>> DecideAsync(AdminSessionCaller caller, TransferReview shown,
        CoverageTransferReviewObservation checkedAtMerchant, TransferApprovalRequest request, string hash,
        DateTimeOffset mfa, CancellationToken ct);
    Task<TransferSummary[]> ListAdminAsync(Guid source, CancellationToken ct);
    Task<ErrorOr<TransferReview>> ReviewAsync(Guid source, Guid transfer, CancellationToken ct);
}

public sealed partial class ApproveCoverageTransferHandler(ICoverageTransferApprovalStore store,
    ICoverageReplacementExceptionStore transactions, ApproveCoverageReplacementHandler manager, CoverageTransferAuthority authority, TimeProvider sessionClock)
{
    public Task<ErrorOr<TransferSummary[]>> ListAsync(AdminSessionCaller caller, Guid source, CancellationToken ct) =>
        InScopeAsync<TransferSummary[]>(caller, source, false, async token => await store.ListAdminAsync(source, token), ct);

    public Task<ErrorOr<TransferReview>> PreviewAsync(AdminSessionCaller caller, Guid source, Guid transfer, CancellationToken ct) =>
        InScopeAsync(caller, source, false, token => store.ReviewAsync(source, transfer, token), ct);

    private Task<ErrorOr<T>> InScopeAsync<T>(AdminSessionCaller caller, Guid source, bool recentMfa,
        Func<CancellationToken, Task<ErrorOr<T>>> work, CancellationToken ct) =>
        transactions.InAdminTransactionAsync<T>(async token =>
        {
            if ((await manager.AuthorizeAsync(caller, recentMfa, token)).Error is { } denied) return denied;
            await transactions.EnterAdminAsync(caller.AdminId, caller.SessionId, source, token);
            var result = await work(token);
            if ((await manager.AuthorizeAsync(caller, recentMfa, token)).Error is { } ended) return ended;
            return result;
        }, ct);
}
