using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Catalogue;

internal sealed class CatalogueHistoryStore(LicensingDbContext db) : ICatalogueHistoryStore
{
    public async Task<IReadOnlyList<(CataloguePreparation Preparation, CataloguePublication Publication)>> ListAsync(
        Guid? after, int take, CancellationToken token)
    {
        var publications = db.Set<CataloguePublication>().AsNoTracking();
        if (after is { } id) publications = publications.Where(row => row.VersionId.CompareTo(id) < 0);
        var page = await (from publication in publications
            join preparation in db.Set<CataloguePreparation>().AsNoTracking()
                on new { publication.AdminId, publication.OperationKey } equals new { preparation.AdminId, preparation.OperationKey }
            orderby publication.VersionId descending
            select new { preparation, publication }).Take(take).ToListAsync(token);
        return page.Select(row => (row.preparation, row.publication)).ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, CatalogueHistoryState>> ReadStatusesAsync(
        Guid[] planVersions, Guid[] trialVersions, CancellationToken token)
    {
        var plans = from version in db.Set<PlanVersion>()
                    where planVersions.Contains(version.Id)
                    join evidence in db.Set<CatalogueRetirement>() on version.Id equals evidence.VersionId into retirements
                    from retirement in retirements.DefaultIfEmpty()
                    select new { version.Id, version.Revision, version.Status, Reason = (string?)retirement.Reason,
                        RetiredAt = (DateTimeOffset?)retirement.RetiredAt };
        var trials = db.Set<TrialPolicyVersion>().Where(version => trialVersions.Contains(version.Id))
            .Select(version => new { version.Id, version.Revision, version.Status, Reason = (string?)null, RetiredAt = (DateTimeOffset?)null });
        return await plans.Concat(trials).ToDictionaryAsync(version => version.Id,
            version => new CatalogueHistoryState(version.Revision, version.Status,
                version.RetiredAt is { } retiredAt ? new PlanRetirementSummary(version.Reason!, retiredAt) : null), token);
    }

    public Task<Guid?> CurrentTrialVersionIdAsync(CancellationToken token) =>
        db.Set<TrialPolicyVersion>().Where(version => version.Status == CatalogueVersionStatus.Published)
            .Select(version => (Guid?)version.Id).SingleOrDefaultAsync(token);
}
