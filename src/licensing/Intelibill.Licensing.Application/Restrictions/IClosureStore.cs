using ErrorOr;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Restrictions;

public interface IClosureStore
{
    Task<ReopeningTarget?> ReadReopeningTargetAsync(Guid account, Guid closure, Guid shop, CancellationToken ct);
    Task<ReopeningOperation?> ReadReopeningOperationAsync(Guid account, Guid operation, CancellationToken ct);
    Task<bool> UsedReopeningConsentAsync(Guid account, Guid shop, string reference, CancellationToken ct);
    Task<ShopClosureStatus> ReadShopClosureStatusAsync(Guid account, Guid shop, CancellationToken ct);
    Task<ProviderClosureV1[]> ReadProviderClosuresAsync(Guid account, CancellationToken ct);
    Task SaveReopeningAsync(NewReopening reopening, CancellationToken ct);
    Task<CheckoutClosureStatus[]> ReadCheckoutClosuresAsync(Guid account, Guid[] shops, CancellationToken ct);
    Task<DateTimeOffset?> ClosedAtAsync(Guid account, Guid shop, CancellationToken ct);
    Task<ClosedBillingAccountV1?> ReadAccountClosureAsync(Guid account, CancellationToken ct);
    Task<bool> AccountClosedAsync(Guid account, CancellationToken ct);
    Task<int?> BillingOwnerRevisionAsync(Guid account, Guid actor, CancellationToken ct);
    Task<SavedClosureProposal?> ProposalAsync(Guid account, Guid? id, DateTimeOffset now, CancellationToken ct);
    Task<SavedClosureProposal?> ProposalByOperationAsync(Guid account, Guid operation, CancellationToken ct);
    Task InsertProposalAsync(SavedClosureProposal proposal, CancellationToken ct);
    Task<Guid[]> ReadNoticesAsync(Guid account, Guid proposal, CancellationToken ct);
    Task<bool> FindConsentOutcomeAsync(OwnerClosureNoticeV1 notice, Guid shop, Guid actor, Guid operation, CancellationToken ct);
    Task RecordNoticeAsync(Guid account, Guid proposal, Guid shop, Guid actor, Guid operation, CancellationToken ct);
    Task LockAccountAsync(Guid account, CancellationToken ct);
    Task<ClosureTargetV1[]> TargetsAsync(Guid account, Guid? shop, CancellationToken ct);
    Task<SavedClosure?> ReadOperationAsync(Guid account, Guid operation, CancellationToken ct);
    Task<ClosureSavedV1> ReadSavedAsync(SavedClosure saved, CancellationToken ct);
    Task<ClosureSavedV1> SaveAsync(SavedClosure saved, ClosureTargetV1[] targets,
        ClosureValidatedV1 consent, CancellationToken ct);
}

public interface IMerchantClosureValidation
{
    Task<ErrorOr<ClosureValidatedV1>> CheckAsync(ClosureValidationV1 validation, CancellationToken ct);
}

public sealed record SavedClosure(Guid Id, Guid AccountId, Guid ActorId, Guid? ShopId, Guid OperationKey,
    string RequestDigest, DateTimeOffset RecordedAt);


public sealed record SavedClosureProposal(Guid Id, Guid AccountId, Guid OperationKey, Guid ActorId, int BillingRevision,
    string ScopeDigest, ClosureTargetV1[] Targets, DateTimeOffset RecordedAt, DateTimeOffset ExpiresAt);
