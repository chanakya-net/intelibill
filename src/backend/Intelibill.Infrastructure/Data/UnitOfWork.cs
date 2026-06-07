using Intelibill.Domain.Interfaces;

namespace Intelibill.Infrastructure.Data;

public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

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
}
