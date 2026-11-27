using Intelibill.Domain.ValueObjects;
namespace Intelibill.Domain.Entities;

public sealed partial class Sale
{
    public IReadOnlyList<SaleExpiryDecision>? ExpiryDecisions { get; private set; }
    public void AttachExpiryDecisions(IReadOnlyList<SaleExpiryDecision> decisions)
    {
        if (ExpiryDecisions is not null) throw new InvalidOperationException("Sale expiry evidence is already frozen.");
        ExpiryDecisions = decisions.Select(x => x with { SaleId = Id }).ToArray();
    }
}
