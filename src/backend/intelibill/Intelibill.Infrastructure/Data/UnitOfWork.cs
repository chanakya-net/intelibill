using Intelibill.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Data;

public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    public bool HasActiveTransaction => context.Database.CurrentTransaction is not null;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    public Task<int> SaveChangesAndCommitAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAndCommitAsync(cancellationToken);

    public void ClearChanges() =>
        context.ChangeTracker.Clear();

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        context.Database.BeginTransactionAsync(cancellationToken);

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
        context.Database.CurrentTransaction is not null
            ? context.Database.CurrentTransaction.CommitAsync(cancellationToken)
            : Task.CompletedTask;

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) =>
        context.Database.CurrentTransaction is not null
            ? context.Database.CurrentTransaction.RollbackAsync(cancellationToken)
            : Task.CompletedTask;

    public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) =>
        context.Database.CurrentTransaction is not null
            ? context.Database.CurrentTransaction.CreateSavepointAsync(name, cancellationToken)
            : Task.CompletedTask;

    public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) =>
        context.Database.CurrentTransaction is not null
            ? context.Database.CurrentTransaction.RollbackToSavepointAsync(name, cancellationToken)
            : Task.CompletedTask;

    public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) =>
        context.Database.CurrentTransaction is not null
            ? context.Database.CurrentTransaction.ReleaseSavepointAsync(name, cancellationToken)
            : Task.CompletedTask;

    public bool IsConcurrencyConflict(Exception exception) =>
        exception is DbUpdateConcurrencyException;

    public bool IsWriteFailure(Exception exception) =>
        exception is DbUpdateException;

    public IReadOnlyList<object> ConflictedEntities(Exception exception) =>
        exception is DbUpdateConcurrencyException concurrencyException
            ? concurrencyException.Entries.Select(entry => entry.Entity).ToList()
            : [];

    public async Task ReloadConflictedEntitiesAsync(Exception exception, CancellationToken cancellationToken = default)
    {
        if (exception is not DbUpdateConcurrencyException concurrencyException)
            return;

        foreach (var entry in concurrencyException.Entries)
            await entry.ReloadAsync(cancellationToken);
    }
}
