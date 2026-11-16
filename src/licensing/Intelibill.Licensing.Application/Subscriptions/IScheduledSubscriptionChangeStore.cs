using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Status;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Subscriptions;

public sealed record CancellationShop(Guid ShopId, Guid RequestId, string ShopName, DateTimeOffset AppliedAt, DateTimeOffset? CoverageEndsAt);

/// <summary>The immutable receipt of one accepted cancellation change, scheduling or withdrawing.</summary>
public sealed record CancellationReceipt(Guid ActorId, string PayloadHash, Guid TermId, string Kind, Guid ChangeId,
    Guid? WithdrawnChangeId, long Revision, DateTimeOffset EffectiveAt, DateTimeOffset RecordedAt, string PolicyVersion,
    int ShopCount)
{
    public const string ScheduleKind = "ScheduleCancellation";
    public const string WithdrawKind = "WithdrawCancellation";

    public SubscriptionCancellationOutcomeV1 Scheduled(Guid account, Guid key) =>
        new(1, account, TermId, key, new(ChangeId, Revision, EffectiveAt, PolicyVersion), ShopCount);

    public SubscriptionCancellationWithdrawalOutcomeV1 Withdrawn(Guid account, Guid key) =>
        new(1, account, TermId, key, WithdrawnChangeId!.Value, Revision, EffectiveAt, RecordedAt, ShopCount);
}

public interface IScheduledSubscriptionChangeStore
{
    Task<TermFactsRow?> ReadTermAsync(Guid account, Guid term, CancellationToken ct);
    Task<CancellationShop[]> ReadAssignedShopsAsync(Guid account, Guid term, CancellationToken ct);
    Task<CancellationShopV1[]> ReadFrozenShopsAsync(Guid account, Guid change, Guid? after, CancellationToken ct);
    Task<CancellationReceipt?> FindReceiptAsync(Guid account, Guid key, CancellationToken ct);
    Task AppendAsync(VerifiedBillingDelegation proof, CancellationSource source, Guid change, long revision,
        Guid operationKey, string payloadHash, DateTimeOffset at, CancellationToken ct);
    Task AppendWithdrawalAsync(VerifiedBillingDelegation proof, CancellationWithdrawal withdrawal, CancellationToken ct);
}

public sealed record CancellationSource(TermFactsRow Term, CancellationShop[] Shops, string ShopHash, string SourceHash);

/// <summary>A withdrawal row and its receipt; the scheduled boundary, scope and policy are copied unchanged.</summary>
public sealed record CancellationWithdrawal(Guid AccountId, Guid TermId, Guid Change, long Revision,
    Domain.Entities.ScheduledSubscriptionChange Withdrawn, Guid OperationKey, string PayloadHash, string SourceHash,
    DateTimeOffset At);
