namespace Intelibill.Domain.Interfaces.Repositories;

/// <summary>What a request for a row lock inside the current transaction came to.</summary>
public enum RowLockOutcome
{
    /// <summary>The row exists in the caller's shop and is now held until the transaction ends.</summary>
    Acquired = 1,

    /// <summary>No such row in the caller's shop. Nothing was locked.</summary>
    NotFound = 2,

    /// <summary>Another transaction held the row longer than the lock budget. Nothing was locked.</summary>
    TimedOut = 3,
}
