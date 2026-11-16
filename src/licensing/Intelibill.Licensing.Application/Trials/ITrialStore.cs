using ErrorOr;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Trials;

public sealed record TrialDeliveryProof(Guid ReplayId, DateTimeOffset ExpiresAt, string Digest);
public sealed record TrialAffiliation(Guid AccountId, Guid? TermId, DateTimeOffset AppliedAt);
public sealed record TrialReceipt(Guid EventId, string Digest, string Status, string? Reason);
public sealed record TrialInterval(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public interface ITrialStore
{
    Task<ErrorOr<ShopTrialDeliveryResultV1>> InDeliveryAsync(ShopTrialConsentV1 consent, TrialDeliveryProof proof,
        Func<TrialAffiliation?, CancellationToken, Task<ErrorOr<ShopTrialDeliveryResultV1>>> apply, CancellationToken ct);
    Task<TrialAffiliation?> UseShopAsync(Guid shop, CancellationToken ct);
    Task UseAccountAsync(Guid account, CancellationToken ct);
    Task<bool> HasHistoryAsync(Guid shop, CancellationToken ct);
    Task<TrialReceipt?> ReadReceiptAsync(ShopTrialConsentV1 consent, CancellationToken ct);
    Task<IReadOnlyList<TrialInterval>> ReadOverlapsAsync(Guid account, TrialInterval interval, CancellationToken ct);
    Task SaveAsync(ShopTrialConsentV1 consent, TrialDeliveryProof proof, ShopTrialDeliveryResultV1 result,
        TrialAffiliation? affiliation, TrialHistory? history, CancellationToken ct);
}

public interface ITrialPolicyReader
{
    Task<ShopTrialPolicyV1?> ReadAsync(Guid? previewReplayId, CancellationToken ct);
}
