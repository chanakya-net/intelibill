using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

public interface ICoverageSlotAssignmentStore
{
    Task<ErrorOr<CoverageSlotAssignmentV1>> RequestAsync(Guid account, Guid term, Guid actor, AssignUnusedCoverageSlotV1 command, CancellationToken ct);
    Task<ErrorOr<CoverageSlotTermV1>> ReadAsync(Guid account, Guid term, int page, CancellationToken ct);
    Task<ErrorOr<CoverageSlotAssignmentV1>> RecoverAsync(Guid account, Guid term, Guid actor, Guid key, CancellationToken ct);
    Task<ErrorOr<CoverageSlotAssignmentV1>> OwnerAsync(Guid account, Guid shop, Guid assignment, CancellationToken ct);
}

public sealed class AssignUnusedCoverageSlotHandler(IBillingDelegationAccess access, ICoverageSlotAssignmentStore store)
{
    public Task<ErrorOr<CoverageSlotAssignmentV1>> RequestAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        AssignUnusedCoverageSlotV1 command, CancellationToken ct) => access.ExecuteAsync<CoverageSlotAssignmentV1>(proof,
            (scope, token) => command.OperationKey != Guid.Empty && command.ShopId != Guid.Empty &&
                command.AcknowledgedSharedEnd && command.AcknowledgedNoNewFee && command.ObservedCapacity > 0
                ? store.RequestAsync(account, term, scope.ActorId, command, token)
                : Task.FromResult<ErrorOr<CoverageSlotAssignmentV1>>(Error.Validation("Coverage.SlotInvalid")), ct);

    public Task<ErrorOr<CoverageSlotTermV1>> ReadAsync(VerifiedBillingDelegation proof, Guid account, Guid term, int page, CancellationToken ct) =>
        access.ExecuteAsync<CoverageSlotTermV1>(proof, (_, token) => store.ReadAsync(account, term, page, token), ct);

    public Task<ErrorOr<CoverageSlotAssignmentV1>> RecoverAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        Guid key, CancellationToken ct) => access.ExecuteAsync<CoverageSlotAssignmentV1>(proof,
            (scope, token) => store.RecoverAsync(account, term, scope.ActorId, key, token), ct);

    public Task<ErrorOr<CoverageSlotAssignmentV1>> OwnerAsync(VerifiedBillingDelegation proof, Guid account, Guid shop,
        Guid assignment, CancellationToken ct) => access.ExecuteAsync<CoverageSlotAssignmentV1>(proof,
            (_, token) => store.OwnerAsync(account, shop, assignment, token), ct);
}
