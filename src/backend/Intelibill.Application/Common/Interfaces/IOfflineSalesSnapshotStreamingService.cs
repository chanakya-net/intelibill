using ErrorOr;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;

namespace Intelibill.Application.Common.Interfaces;

public interface IOfflineSalesSnapshotStreamingService
{
    Task<ErrorOr<Success>> ValidateAccessAsync(
        Guid userId,
        Guid activeShopId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<IOfflineSalesSnapshotStreamRecord> StreamAsync(
        Guid activeShopId,
        Guid snapshotId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);
}

