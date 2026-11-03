using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IOfflineSubmissionRepository : IRepository<OfflineSubmission>
{
    /// <summary>
    /// Stores the envelope unless one with the same shop, device, client sale id and payload hash
    /// exists, and returns the stored (first) row, tracked. Joins any open transaction.
    /// </summary>
    Task<OfflineSubmission> RecordAsync(OfflineSubmission candidate, CancellationToken cancellationToken);

    Task<OfflineSubmission?> FindEarliestByHashAsync(
        Guid shopId,
        string deviceId,
        string clientSaleId,
        string payloadHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// True when the user reserved a lease on the device or submitted a synced envelope from it.
    /// Membership is checked by the caller.
    /// </summary>
    Task<bool> HasDeviceHistoryAsync(Guid shopId, string deviceId, Guid userId, CancellationToken cancellationToken);
}
