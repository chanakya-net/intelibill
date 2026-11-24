using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

/// <summary>Append-only conflict observations recorded for a reconciliation case's historical licence review.</summary>
public interface ILicenseHistoryReviewObservationRepository
{
    /// <summary>
    /// Appends the observation unless an observation with the same fingerprint already exists on the case.
    /// Returns true only when a new distinct observation row was inserted.
    /// </summary>
    Task<bool> RecordAsync(LicenseHistoryReviewObservation observation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicenseHistoryReviewObservation>> GetByCaseAsync(
        Guid shopId,
        Guid caseId,
        CancellationToken cancellationToken = default);

    Task<bool> HasAnyAsync(Guid shopId, Guid caseId, CancellationToken cancellationToken = default);
}
