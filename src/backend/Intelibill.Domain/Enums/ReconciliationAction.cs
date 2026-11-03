namespace Intelibill.Domain.Enums;

/// <summary>
/// An action an authorized person takes on a reconciliation case. Stored as its number (the closing
/// action is kept on the case); never renumber a member.
/// </summary>
public enum ReconciliationAction
{
    AcknowledgeVariance = 1,
    Escalate = 2,
    LinkExistingSale = 3,
    LinkStockCorrection = 4,
    RetryHistoricalLease = 5,
}
