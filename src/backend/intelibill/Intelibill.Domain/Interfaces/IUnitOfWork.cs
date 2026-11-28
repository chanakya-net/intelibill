namespace Intelibill.Domain.Interfaces;

public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAndCommitAsync(CancellationToken cancellationToken = default);
    void ClearChanges();
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
    Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default);
    Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default);
    Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> is the persistence layer's
    /// optimistic-concurrency failure: a save affected fewer rows than expected because a concurrency
    /// token changed. Only the exception itself is classified; inner and aggregate exceptions are not unwrapped.
    /// </summary>
    bool IsConcurrencyConflict(Exception exception);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> is a persistence-layer failure to write
    /// changes to the database. A concurrency conflict is also a write failure. Only the exception itself is
    /// classified; inner and aggregate exceptions are not unwrapped.
    /// </summary>
    bool IsWriteFailure(Exception exception);

    /// <summary>
    /// Returns the tracked entities involved in a concurrency conflict, in the order the persistence layer
    /// reported them. Returns an empty list for any exception that is not a concurrency conflict.
    /// </summary>
    IReadOnlyList<object> ConflictedEntities(Exception exception);

    /// <summary>
    /// Reloads, one after another and in reported order, each tracked entity involved in a concurrency
    /// conflict from the database so a retry works on current values. Does nothing for any exception that
    /// is not a concurrency conflict.
    /// </summary>
    Task ReloadConflictedEntitiesAsync(Exception exception, CancellationToken cancellationToken = default);
}
