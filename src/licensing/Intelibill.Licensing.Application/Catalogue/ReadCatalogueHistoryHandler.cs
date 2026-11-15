using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Catalogue;

public sealed record CatalogueHistoryItem(Guid? PlanId, Guid VersionId, int Version, string Status, JsonElement Snapshot,
    DateTimeOffset PublishedAt);
public sealed record CatalogueHistoryPage(CatalogueHistoryItem[] Items, Guid? NextCursor, Guid? CurrentTrialVersionId);

public interface ICatalogueHistoryStore
{
    Task<IReadOnlyList<(CataloguePreparation Preparation, CataloguePublication Publication)>> ListAsync(Guid? after,
        int take, CancellationToken token);
    Task<IReadOnlyDictionary<Guid, (int Revision, CatalogueVersionStatus Status)>> ReadStatusesAsync(
        Guid[] planVersions, Guid[] trialVersions, CancellationToken token);
    Task<Guid?> CurrentTrialVersionIdAsync(CancellationToken token);
}

public sealed class ReadCatalogueHistoryHandler(ICatalogueDraftStore drafts, ICatalogueHistoryStore history, TimeProvider clock)
{
    private const int PageSize = 50;

    public Task<ErrorOr<CatalogueHistoryPage>> HandleAsync(AdminSessionCaller caller, Guid? after, CancellationToken token) =>
        drafts.InTransactionAsync<CatalogueHistoryPage>(async ct =>
        {
            if (await CatalogueAuthority.EnterAsync(drafts, caller, clock, ct) is { } denied) return denied;
            var rows = await history.ListAsync(after, PageSize + 1, ct);
            var page = rows.Take(PageSize).ToArray();
            var statuses = await history.ReadStatusesAsync(
                page.Where(row => row.Preparation.PlanId is not null).Select(row => row.Preparation.VersionId).ToArray(),
                page.Where(row => row.Preparation.PlanId is null).Select(row => row.Preparation.VersionId).ToArray(), ct);
            var items = page.Select(row => ToItem(row.Preparation, row.Publication, statuses[row.Preparation.VersionId])).ToArray();
            return new CatalogueHistoryPage(items, rows.Count > items.Length ? items[^1].VersionId : null,
                await history.CurrentTrialVersionIdAsync(ct));
        }, token);

    private static CatalogueHistoryItem ToItem(CataloguePreparation prep, CataloguePublication publication,
        (int Revision, CatalogueVersionStatus Status) version) =>
        new(prep.PlanId, prep.VersionId, version.Revision, version.Status == CatalogueVersionStatus.Published ? "published" : "retired",
            CataloguePublicationOperation.View(prep, publication.PublishedAt).Snapshot, publication.PublishedAt);
}
