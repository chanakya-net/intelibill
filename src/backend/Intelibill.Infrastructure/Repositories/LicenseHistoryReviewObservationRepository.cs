using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class LicenseHistoryReviewObservationRepository(ApplicationDbContext context)
    : ILicenseHistoryReviewObservationRepository
{
    public async Task<bool> RecordAsync(LicenseHistoryReviewObservation observation, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
        {
            var exists = await context.LicenseHistoryReviewObservations.AnyAsync(
                row => row.ShopId == observation.ShopId
                    && row.CaseId == observation.CaseId
                    && row.Fingerprint == observation.Fingerprint,
                cancellationToken);
            if (exists) return false;
            context.LicenseHistoryReviewObservations.Add(observation);
            return true;
        }

        var inserted = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.license_history_review_observations
                (id, shop_id, case_id, offline_submission_id, observed_at, reason_code, subreason,
                 compared_field, window_start, window_end, observed_account_revision, observed_shop_revision,
                 source_completion_cutoff, competing_submission_id, fingerprint)
            VALUES
                ({observation.Id}, {observation.ShopId}, {observation.CaseId}, {observation.OfflineSubmissionId},
                 {observation.ObservedAt}, {observation.ReasonCode}, {observation.Subreason},
                 {observation.ComparedField}, {observation.WindowStart}, {observation.WindowEnd},
                 {observation.ObservedAccountRevision}, {observation.ObservedShopRevision},
                 {observation.SourceCompletionCutoff}, {observation.CompetingSubmissionId}, {observation.Fingerprint})
            ON CONFLICT (shop_id, case_id, fingerprint) DO NOTHING
            """,
            cancellationToken);
        return inserted == 1;
    }

    public async Task<IReadOnlyList<LicenseHistoryReviewObservation>> GetByCaseAsync(
        Guid shopId,
        Guid caseId,
        CancellationToken cancellationToken = default) =>
        await context.LicenseHistoryReviewObservations
            .AsNoTracking()
            .Where(row => row.ShopId == shopId && row.CaseId == caseId)
            .OrderBy(row => row.ObservedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> HasAnyAsync(Guid shopId, Guid caseId, CancellationToken cancellationToken = default) =>
        context.LicenseHistoryReviewObservations
            .AsNoTracking()
            .AnyAsync(row => row.ShopId == shopId && row.CaseId == caseId, cancellationToken);
}
