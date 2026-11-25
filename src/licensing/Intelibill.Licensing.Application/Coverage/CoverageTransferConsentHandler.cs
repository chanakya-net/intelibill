using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

/// <summary>
/// A billing owner's explicit accept or revoke of exactly one purpose of one proposal. Both billing accounts are
/// locked in canonical order before the live owner row, so a consent can never race an ownership change on either
/// side. The decision binds the displayed proposal revision, terms digest and the owner's membership revision.
/// </summary>
public sealed class CoverageTransferConsentHandler(IBillingDelegationAccess access, ICoverageTransferStore store)
{
    public Task<ErrorOr<CoverageTransferV1>> DecideAsync(VerifiedBillingDelegation proof, Guid account, Guid transfer,
        string purpose, string kind, CoverageTransferDecisionV1 decision, CancellationToken ct) =>
        access.ExecutePairAsync<CoverageTransferV1>(proof,
            token => store.CounterpartyAsync(account, transfer, token),
            (scope, token) => decision.IsValid()
                ? store.DecideAsync(account, scope.ActorId, transfer, purpose, kind, decision, token)
                : Task.FromResult<ErrorOr<CoverageTransferV1>>(CoverageTransferErrors.Invalid), ct);

    public Task<ErrorOr<CoverageTransferV1>> RecoverAsync(VerifiedBillingDelegation proof, Guid account, Guid transfer,
        string purpose, Guid key, CancellationToken ct) =>
        access.ExecuteAsync<CoverageTransferV1>(proof,
            (scope, token) => store.RecoverDecisionAsync(account, scope.ActorId, transfer, purpose, key, token), ct);
}
