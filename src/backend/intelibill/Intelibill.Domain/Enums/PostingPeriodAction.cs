namespace Intelibill.Domain.Enums;

/// <summary>A change of a posting period's state, kept in its audit trail. Stored as its number.</summary>
public enum PostingPeriodAction
{
    Closed = 1,
    Reopened = 2,
}
