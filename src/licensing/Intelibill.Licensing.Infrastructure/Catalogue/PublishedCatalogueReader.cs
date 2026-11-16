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
        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('app.catalogue_public_read', {replayId.ToString("D")}, true)", token);
        var offers = db.Set<PublishedCatalogueOffer>().AsNoTracking()
            .Where(offer => offer.DevelopmentFixture == developmentFixture);
        if (after is { } id) offers = offers.Where(offer => offer.PlanId.CompareTo(id) > 0);
        var rows = await offers.Where(candidate => !offers.Any(newer =>
                newer.PlanId == candidate.PlanId && newer.VersionNumber > candidate.VersionNumber))
            .OrderBy(offer => offer.PlanId).Take(take).ToListAsync(token);
        return rows.Select(row => JsonSerializer.Deserialize<PublishedPlanTermsV1>(row.Snapshot, Json)!).ToArray();
    }
}
