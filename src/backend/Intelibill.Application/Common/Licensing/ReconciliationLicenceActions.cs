using Intelibill.Domain.Enums;

namespace Intelibill.Application.Common.Licensing;

public static class ReconciliationLicenceActions
{
    public static LicenceAction For(ReconciliationAction action) => action switch
    {
        ReconciliationAction.AcknowledgeVariance or ReconciliationAction.Escalate or
        ReconciliationAction.LinkExistingSale or ReconciliationAction.LinkStockCorrection or
        ReconciliationAction.RetryHistoricalLease or ReconciliationAction.ReconcileTillSale => LicenceAction.Recovery,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unclassified reconciliation action."),
    };
}
