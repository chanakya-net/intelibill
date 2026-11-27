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
            return await closures.ReadProviderClosuresAsync(account, token);
        }, ct);
}
