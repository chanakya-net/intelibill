using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Status;

public sealed partial class ReadProviderStatusHandler
{
    public Task<ErrorOr<ProviderClosureV1[]>> ReadClosuresAsync(AdminSessionCaller caller, Guid account, CancellationToken ct) =>
        store.InProviderTransactionAsync<ProviderClosureV1[]>(async token =>
        {
            var grant = await AuthorizeAsync(caller, token);
            if (grant.IsError) return grant.Errors;
            await store.UseAccountAsync(account, token);
            var saved = await closures.ListSavedAsync(account, token);
            var details = new List<ProviderClosureV1>(saved.Length);
            foreach (var row in saved)
            {
                var sync = await synchronization.ReadAsync(account, [.. row.Shops.Select(shop => shop.ShopId)], token, row.Id);
                details.Add(new(row.Id, row.Scope, row.SavedAt, row.FinancialTreatment,
                    [.. row.Shops.Select(shop => ProjectClosure(shop, sync.GetValueOrDefault(shop.ShopId)))]));
            }
            return details.ToArray();
        }, ct);

    private static ProviderClosureShopV1 ProjectClosure(ClosureTargetV1 shop, EntitlementSynchronizationV1? sync)
    {
        var state = sync?.AppliedShopRevision is > 0 ? "Applied" : sync?.State ?? "Pending";
        return new(shop.ShopId, shop.ShopName, state, sync?.SavedShopRevision, sync?.AppliedShopRevision, sync?.LastSynchronizedAt);
    }
}
