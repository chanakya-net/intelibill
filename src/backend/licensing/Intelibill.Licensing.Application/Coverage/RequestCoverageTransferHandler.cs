using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

public static class CoverageTransferErrors
{
    public static Error Invalid => Error.Validation("Coverage.TransferInvalid",
        "Check the destination account, shop, remaining-access treatment, reason and acknowledgements.");
    public static Error NotFound => Error.NotFound("Coverage.TransferNotFound",
        "The coverage transfer request could not be found.");
    public static Error Changed => Error.Conflict("Coverage.TransferChanged",
        "The coverage, dates, permits or ownership changed. Review the latest details before confirming.");
    public static Error Conflict => Error.Conflict("Coverage.TransferConflict",
        "This shop already has an open coverage transfer request.");
    public static Error ConsentInvalid => Error.Conflict("Coverage.TransferConsentInvalid",
        "This consent cannot be recorded in its current state.");
    public static Error AuthorityChanged => Error.Conflict("Coverage.TransferAuthorityChanged",
        "Your billing ownership changed since this decision. Review the request and decide again.");
    public static Error PurposeDenied => Error.Forbidden("Coverage.TransferPurposeDenied",
        "Only the live owner named by this consent can decide it.");
}

/// <summary>
/// Persistence port for transfer proposals and billing-owner consents. Every method runs inside the caller's
/// delegation transaction, after live Billing Owner authorization and under both account locks for writes.
/// </summary>
public interface ICoverageTransferStore
{
    /// <summary>The other billing account of a proposal the caller's account is party to, for canonical pair locking.</summary>
    Task<Guid?> CounterpartyAsync(Guid account, Guid transfer, CancellationToken ct);
    Task<ErrorOr<CoverageTransferPreviewV1>> PreviewAsync(Guid source, Guid term, Guid actor,
        PreviewCoverageTransferV1 query, CancellationToken ct);
    Task<ErrorOr<CoverageTransferV1>> RequestAsync(Guid source, Guid term, Guid actor,
        RequestCoverageTransferV1 command, CancellationToken ct);
    Task<ErrorOr<CoverageTransferV1>> RecoverAsync(Guid source, Guid term, Guid actor, Guid key, CancellationToken ct);
    Task<ErrorOr<CoverageTransferListV1>> ListAsync(Guid account, Guid actor, CancellationToken ct);
    Task<ErrorOr<CoverageTransferV1>> ReadAsync(Guid account, Guid actor, Guid transfer, CancellationToken ct);
    Task<ErrorOr<CoverageTransferOwnerTermsV1>> OwnerReadAsync(Guid source, Guid shop, Guid transfer, CancellationToken ct);
    Task<ErrorOr<CoverageTransferV1>> DecideAsync(Guid account, Guid actor, Guid transfer, string purpose, string kind,
        CoverageTransferDecisionV1 decision, CancellationToken ct);
    Task<ErrorOr<CoverageTransferV1>> RecoverDecisionAsync(Guid account, Guid actor, Guid transfer, string purpose,
        Guid key, CancellationToken ct);
}

/// <summary>
/// Records a proposed source/destination transfer of one named shop. The request is evidence only: it carries
/// zero consents and never touches affiliation, coverage, grants, payments or entitlement publication.
/// </summary>
public sealed class RequestCoverageTransferHandler(IBillingDelegationAccess access, ICoverageTransferStore store)
{
    public Task<ErrorOr<CoverageTransferPreviewV1>> PreviewAsync(VerifiedBillingDelegation proof, Guid source, Guid term,
        PreviewCoverageTransferV1 query, CancellationToken ct) =>
        access.ExecuteAsync<CoverageTransferPreviewV1>(proof, (scope, token) => IsValid(source, query)
            ? store.PreviewAsync(source, term, scope.ActorId, query, token)
            : Task.FromResult<ErrorOr<CoverageTransferPreviewV1>>(CoverageTransferErrors.Invalid), ct);

    public Task<ErrorOr<CoverageTransferV1>> RequestAsync(VerifiedBillingDelegation proof, Guid source, Guid term,
        RequestCoverageTransferV1 command, CancellationToken ct) =>
        access.ExecutePairAsync<CoverageTransferV1>(proof,
            _ => Task.FromResult<Guid?>(command.DestinationAccountId),
            (scope, token) => command.IsValid() && command.DestinationAccountId != source
                ? store.RequestAsync(source, term, scope.ActorId, command, token)
                : Task.FromResult<ErrorOr<CoverageTransferV1>>(CoverageTransferErrors.Invalid), ct);

    public Task<ErrorOr<CoverageTransferV1>> RecoverAsync(VerifiedBillingDelegation proof, Guid source, Guid term,
        Guid key, CancellationToken ct) =>
        access.ExecuteAsync<CoverageTransferV1>(proof,
            (scope, token) => store.RecoverAsync(source, term, scope.ActorId, key, token), ct);

    public Task<ErrorOr<CoverageTransferListV1>> ListAsync(VerifiedBillingDelegation proof, Guid account, CancellationToken ct) =>
        access.ExecuteAsync<CoverageTransferListV1>(proof,
            (scope, token) => store.ListAsync(account, scope.ActorId, token), ct);

    public Task<ErrorOr<CoverageTransferV1>> ReadAsync(VerifiedBillingDelegation proof, Guid account, Guid transfer,
        CancellationToken ct) =>
        access.ExecuteAsync<CoverageTransferV1>(proof,
            (scope, token) => store.ReadAsync(account, scope.ActorId, transfer, token), ct);

    public Task<ErrorOr<CoverageTransferOwnerTermsV1>> OwnerAsync(VerifiedBillingDelegation proof, Guid source, Guid shop,
        Guid transfer, CancellationToken ct) =>
        access.ExecuteAsync<CoverageTransferOwnerTermsV1>(proof,
            (_, token) => store.OwnerReadAsync(source, shop, transfer, token), ct);

    private static bool IsValid(Guid source, PreviewCoverageTransferV1 q) =>
        q.DestinationAccountId != Guid.Empty && q.DestinationAccountId != source && q.ShopId != Guid.Empty &&
        CoverageTransfersV1.IsTreatment(q.Treatment) &&
        (q.Treatment == CoverageTransfersV1.ReviewedAllocation) == (q.CutoverAt is not null) &&
        (q.CutoverAt is null || EntitlementSnapshotsV1.IsInstant(q.CutoverAt.Value));
}
