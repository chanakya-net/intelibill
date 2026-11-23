using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IOfflineSubmissionRepository : IRepository<OfflineSubmission>
{
    /// <summary>
    /// Stores the envelope unless one with the same shop, device, client sale id and payload hash
    /// exists, and returns the stored (first) row, tracked. Joins any open transaction.
    /// </summary>
    Task<OfflineSubmission> RecordAsync(OfflineSubmission candidate, CancellationToken cancellationToken);

    /// <summary>
    /// Serializes envelope registration and case selection for one (shop, device) inside the open
    /// transaction using a bounded PostgreSQL advisory lock.
    /// </summary>
    Task<RowLockOutcome> LockDeviceIntakeAsync(Guid shopId, string deviceId, CancellationToken cancellationToken);

    /// <summary>Reads the committed envelope row within the shop, tracked for a subsequent sale link.</summary>
    Task<OfflineSubmission?> GetTrackedByIdAsync(Guid shopId, Guid submissionId, CancellationToken cancellationToken);

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
