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

    public async Task<IReadOnlyDictionary<Guid, (int Revision, CatalogueVersionStatus Status)>> ReadStatusesAsync(
        Guid[] planVersions, Guid[] trialVersions, CancellationToken token)
    {
        var plans = db.Set<PlanVersion>().Where(version => planVersions.Contains(version.Id))
            .Select(version => new { version.Id, version.Revision, version.Status });
        var trials = db.Set<TrialPolicyVersion>().Where(version => trialVersions.Contains(version.Id))
            .Select(version => new { version.Id, version.Revision, version.Status });
        return await plans.Concat(trials).ToDictionaryAsync(version => version.Id, version => (version.Revision, version.Status), token);
    }

    public Task<Guid?> CurrentTrialVersionIdAsync(CancellationToken token) =>
        db.Set<TrialPolicyVersion>().Where(version => version.Status == CatalogueVersionStatus.Published)
            .Select(version => (Guid?)version.Id).SingleOrDefaultAsync(token);
}
