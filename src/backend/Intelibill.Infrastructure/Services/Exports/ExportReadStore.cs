using System.Data;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Intelibill.Infrastructure.Services.Exports;

/// <summary>One request owns one read-only database snapshot, including preflight and every dependency read.</summary>
public sealed class ExportReadStore(ApplicationDbContext context) : IExportReadStore
{
    public async Task<IExportReadSnapshot> OpenSnapshotAsync(CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Sales exports require their own read-only repeatable-read transaction.");
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
            return new ExportReadSnapshot(context, transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}

internal sealed partial class ExportReadSnapshot(ApplicationDbContext db, IDbContextTransaction transaction) : IExportReadSnapshot
{
    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
