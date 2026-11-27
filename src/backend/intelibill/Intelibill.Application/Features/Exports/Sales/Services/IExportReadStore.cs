namespace Intelibill.Application.Features.Exports.Sales.Services;

public interface IExportReadStore
{
    Task<IExportReadSnapshot> OpenSnapshotAsync(CancellationToken cancellationToken);
}

public interface IExportReadSnapshot : IAsyncDisposable
{
    Task<ExportCounts> CountAsync(Guid shopId, ExportWindow window, string level, string format, CancellationToken cancellationToken);
    Task<ExportPage> ReadPageAsync(Guid shopId, ExportWindow window, string level, ExportCursor? cursor, int pageSize, CancellationToken cancellationToken);
}
