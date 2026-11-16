using System.Text.Json;
using Intelibill.Licensing.Application.Catalogue;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Catalogue;

internal sealed class PublishedCatalogueReader(LicensingDbContext db) : IPublishedCatalogueReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PublishedPlanTermsV1>> ListAsync(Guid replayId, Guid? after, int take, bool developmentFixture,
        CancellationToken token)
    {
        await EnterReadAsync(replayId, token);
        var offers = Offers(developmentFixture);
        if (after is { } id) offers = offers.Where(offer => offer.PlanId.CompareTo(id) > 0);
        var rows = await offers.Where(candidate => !offers.Any(newer =>
                newer.PlanId == candidate.PlanId && newer.VersionNumber > candidate.VersionNumber)
                && !db.Set<WithdrawnCatalogueOffer>().Any(withdrawn => withdrawn.VersionId == candidate.VersionId))
            .OrderBy(offer => offer.PlanId).Take(take).ToListAsync(token);
        return rows.Select(row => JsonSerializer.Deserialize<PublishedPlanTermsV1>(row.Snapshot, Json)!).ToArray();
    }

    public async Task<PublishedPlanVersionV1?> ReadVersionAsync(Guid replayId, Guid versionId, bool developmentFixture,
        bool selectionEnabled, CancellationToken token)
    {
        await EnterReadAsync(replayId, token);
        var offers = Offers(developmentFixture);
        var row = await offers.Where(offer => offer.VersionId == versionId).Select(offer => new
        {
            offer.Snapshot,
            Available = selectionEnabled && !db.Set<WithdrawnCatalogueOffer>().Any(marker => marker.VersionId == versionId)
                && !offers.Any(newer => newer.PlanId == offer.PlanId && newer.VersionNumber > offer.VersionNumber),
        }).SingleOrDefaultAsync(token);
        return row is null ? null : new PublishedPlanVersionV1(
            JsonSerializer.Deserialize<PublishedPlanTermsV1>(row.Snapshot, Json)!, row.Available);
    }

    private IQueryable<PublishedCatalogueOffer> Offers(bool developmentFixture) =>
        db.Set<PublishedCatalogueOffer>().AsNoTracking().Where(offer => offer.DevelopmentFixture == developmentFixture);

    private Task<int> EnterReadAsync(Guid replayId, CancellationToken token) => db.Database.ExecuteSqlAsync(
        $"SELECT set_config('app.catalogue_public_read', {replayId.ToString("D")}, true)", token);
}
