using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public Task<ErrorOr<AccountAffiliationsV1>> ListAsync(MerchantBillingCaller caller, Guid account, CancellationToken ct) =>
        ReadAffiliationAsync<AccountAffiliationsV1>(caller,
            new(ShopAffiliationsV1.Read, HttpMethod.Get, ShopAffiliationsV1.AccountPath(account), account, []),
            result => result.AccountId == account && result.AccountName is { Length: > 0 } &&
                result.Terms is { Length: <= ShopAffiliationsV1.PageSize } &&
                result.Requests is { Length: <= ShopAffiliationsV1.PageSize } &&
                result.Requests.All(request => request.AccountId == account), ct);

    public Task<ErrorOr<ShopAffiliationRequestV1>> RequestAsync(MerchantBillingCaller caller, Guid account,
        RequestShopAffiliationV1 request, CancellationToken ct) =>
        ReadAffiliationAsync<ShopAffiliationRequestV1>(caller, new(ShopAffiliationsV1.Request, HttpMethod.Post,
            ShopAffiliationsV1.AccountPath(account), account, JsonSerializer.SerializeToUtf8Bytes(request, Json)),
            result => result.RequestId != Guid.Empty && result.AccountId == account && result.ShopId == request.ShopId
                && result.Term?.TermId == request.TermId, ct);

    public async Task<ErrorOr<ShopAffiliationConsentV1>> ConsentAsync(MerchantBillingCaller caller, Guid account, Guid shop,
        Guid request, ShopAffiliationConsentAction action, Guid operation, CancellationToken ct)
    {
        if (action != ShopAffiliationConsentAction.Read && operation == Guid.Empty)
            return MerchantBillingErrors.OperationKeyInvalid;
        try
        {
            if (!await consents.IsOwnerAsync(caller, shop, ct))
                return ShopAffiliationConsentErrors.OwnerRequired;
            var detail = await ReadAffiliationAsync<ShopAffiliationRequestV1>(caller, new(ShopAffiliationsV1.OwnerRead,
                HttpMethod.Get, ShopAffiliationsV1.OwnerPath(account, shop, request), null, []),
                result => result.AccountId == account && result.ShopId == shop && result.RequestId == request, ct);
            return detail.IsError ? detail.Errors : await consents.RunAsync(caller, detail.Value, action, operation, ct);
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Unavailable("AffiliationStorageUnavailable", error);
        }
    }

    private Task<ErrorOr<T>> ReadAffiliationAsync<T>(MerchantBillingCaller caller, BillingDelegationIntent intent,
        Func<T, bool> valid, CancellationToken ct) where T : class =>
        RunAsync<T>(caller, intent, ShopAffiliationsV1.ResponseLimit, async (response, token) =>
        {
            var result = await ReadJsonAsync<T>(response, token);
            return result is not null && valid(result) ? result : Unavailable("InvalidAffiliationContract");
        }, ct);
}
