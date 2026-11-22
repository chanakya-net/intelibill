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

    /// <summary>Active-member seats per shop from the fenced projection; null when unknown or unprojected.</summary>
    public int? MemberAllowance { get; init; }

    public Func<LicenceDecision>? Reevaluate { get; init; }

    /// <summary>The binding and retained proofs behind the fenced heads; null when the shop is unprojected.</summary>
    public Guid? AffiliationRequestId { get; init; }

    public Guid? AccountEvidenceId { get; init; }

    public Guid? ShopEvidenceId { get; init; }

    // The same projection is fenced for the transaction, but time can advance while acquiring business locks.
    public LicenceDecision CurrentDecision() => Reevaluate?.Invoke() ?? Decision;
}
