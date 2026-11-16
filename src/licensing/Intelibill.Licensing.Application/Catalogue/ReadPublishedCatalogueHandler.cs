using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Catalogue;

public interface IPublishedCatalogueReader
{
    Task<IReadOnlyList<PublishedPlanTermsV1>> ListAsync(Guid replayId, Guid? after, int take, bool developmentFixture,
        CancellationToken token);
    Task<PublishedPlanVersionV1?> ReadVersionAsync(Guid replayId, Guid versionId, bool developmentFixture,
        bool selectionEnabled, CancellationToken token);
}

public sealed class ReadPublishedCatalogueHandler(IBillingDelegationAccess access, IPublishedCatalogueReader reader,
    ICataloguePublicationApproval approval)
{
    public Task<ErrorOr<PublishedCatalogueV1>> HandleAsync(VerifiedBillingDelegation delegation, Guid? after,
        CancellationToken token)
    {
        if (delegation is not { Action: PublishedCatalogueV1.Read, AccountId: null }
            || delegation.Path != PublishedCatalogueV1.ReadPath(after) || after == Guid.Empty)
            return Task.FromResult<ErrorOr<PublishedCatalogueV1>>(BillingDelegationErrors.Invalid);

        return access.ExecuteAsync<PublishedCatalogueV1>(delegation, async (_, ct) =>
        {
            if (approval.PublicFixtureFilter is not { } filter)
                return new PublishedCatalogueV1([], null);
            var rows = await reader.ListAsync(delegation.ReplayId, after, PublishedCatalogueV1.PageSize + 1, filter, ct);
            var items = rows.Take(PublishedCatalogueV1.PageSize).ToArray();
            return new PublishedCatalogueV1(items, rows.Count > items.Length ? items[^1].PlanId : null);
        }, token);
    }

    private static Error VersionNotFound => Error.NotFound("Billing.PlanVersionNotFound", "The plan version was not found.");

    public async Task<ErrorOr<PublishedPlanVersionV1>> ReadVersionAsync(VerifiedBillingDelegation delegation, Guid versionId,
        CancellationToken token)
    {
        if (delegation is not { Action: PublishedCatalogueV1.Read, AccountId: null }
            || delegation.Path != PublishedCatalogueV1.VersionPath(versionId) || versionId == Guid.Empty)
            return BillingDelegationErrors.Invalid;

        var filter = approval.HistoricalFixtureFilter;
        var result = await access.ExecuteAsync<PublishedPlanVersionV1?>(delegation, async (_, ct) =>
            await reader.ReadVersionAsync(delegation.ReplayId, versionId, filter, approval.PublicFixtureFilter == filter, ct),
            token);
        if (result.IsError) return result.Errors;
        return result.Value is { } version ? version : VersionNotFound;
    }
}
