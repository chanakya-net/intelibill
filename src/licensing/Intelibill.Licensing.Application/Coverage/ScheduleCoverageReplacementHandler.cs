using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Coverage;

public interface ICoverageReplacementStore
{
    Task<ErrorOr<CoverageReplacementV1>> OwnerAsync(Guid account, Guid shop, Guid replacement, CancellationToken ct);
    Task<ErrorOr<CoverageReplacementV1>> ScheduleAsync(Guid account, Guid term, Guid actor, ScheduleCoverageReplacementV1 command, CancellationToken ct);
    Task<ErrorOr<CoverageReplacementV1>> RecoverAsync(Guid account, Guid term, Guid actor, Guid key, CancellationToken ct);
    Task<ErrorOr<CoverageReplacementPreviewV1>> ReadAsync(Guid account, Guid term, int page, CancellationToken ct);
}

public sealed class ScheduleCoverageReplacementHandler(IBillingDelegationAccess access, ICoverageReplacementStore store)
{
    public Task<ErrorOr<CoverageReplacementPreviewV1>> ReadAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        int page, CancellationToken ct) => access.ExecuteAsync<CoverageReplacementPreviewV1>(proof,
            (_, token) => store.ReadAsync(account, term, page, token), ct);

    public Task<ErrorOr<CoverageReplacementV1>> ScheduleAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        ScheduleCoverageReplacementV1 command, CancellationToken ct) => access.ExecuteAsync<CoverageReplacementV1>(proof,
            (scope, token) => command.OperationKey != Guid.Empty && command.OutgoingShopId != Guid.Empty &&
                command.IncomingShopId != Guid.Empty && command.OutgoingShopId != command.IncomingShopId &&
                command.ObservedCapacity > 0 && command.ObservedMemberAllowance > 0 &&
                EntitlementSnapshotsV1.IsInstant(command.ObservedEffectiveAt) &&
                (command.ObservedPermitDeadline is null || EntitlementSnapshotsV1.IsInstant(command.ObservedPermitDeadline.Value)) &&
                command.AcknowledgedRenewal && command.AcknowledgedPermitWait && command.AcknowledgedShopSelection
                ? store.ScheduleAsync(account, term, scope.ActorId, command, token)
                : Task.FromResult<ErrorOr<CoverageReplacementV1>>(Error.Validation("Coverage.ReplacementInvalid")), ct);

    public Task<ErrorOr<CoverageReplacementV1>> RecoverAsync(VerifiedBillingDelegation proof, Guid account, Guid term,
        Guid key, CancellationToken ct) => access.ExecuteAsync<CoverageReplacementV1>(proof,
            (scope, token) => store.RecoverAsync(account, term, scope.ActorId, key, token), ct);

    public Task<ErrorOr<CoverageReplacementV1>> OwnerAsync(VerifiedBillingDelegation proof, Guid account, Guid shop,
        Guid replacement, CancellationToken ct) => access.ExecuteAsync<CoverageReplacementV1>(proof,
            (_, token) => store.OwnerAsync(account, shop, replacement, token), ct);
}
