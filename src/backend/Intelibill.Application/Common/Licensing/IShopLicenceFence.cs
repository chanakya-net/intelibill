using ErrorOr;

namespace Intelibill.Application.Common.Licensing;

/// <summary>
/// Called after live merchant authority and any operation receipt/reconciliation lock, within the
/// source's read-committed transaction. Take this before period or business locks. The caller
/// applies the decision and any source eligibility checks using the existing business locks.
/// </summary>
public interface IShopLicenceFence
{
    Task<ErrorOr<ShopLicenceLease>> EnterAsync(Guid shopId, LicenceAction action, CancellationToken cancellationToken);
}

public readonly record struct ShopLicenceLease(Guid ShopId, Guid? AccountId, long? AccountRevision, long? ShopRevision,
    LicenceDecision Decision)
{
    public string? EffectiveSuspension { get; init; }
}
