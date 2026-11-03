namespace Intelibill.Domain.Enums;

/// <summary>Lifecycle of a reconciliation case. Stored as its number; escalated cases are still unresolved.</summary>
public enum ReconciliationCaseStatus
{
    Open = 1,
    Escalated = 2,
    Resolved = 3,
}
