using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public Task<ErrorOr<ShopClosureReviewV1>> PreviewAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct) =>
        closures.PreviewAsync(caller, shop, ct);

    public async Task<ErrorOr<ClosureSavedV1>> CloseAsync(MerchantBillingCaller caller, Guid shop,
        ShopClosureConfirmationV1 confirmation, CancellationToken ct)
    {
        var recorded = await closures.ConfirmAsync(caller, shop, confirmation, null, ct);
        if (recorded.IsError) return recorded.Errors;
        return await ReadClosureAsync<ClosureSavedV1>(caller, new(ShopClosuresV1.ShopAction, HttpMethod.Post,
            ShopClosuresV1.ShopPath(shop), null, JsonSerializer.SerializeToUtf8Bytes(recorded.Value.Request, Json)),
            saved => saved.Scope == "Shop" && saved.AccountId == recorded.Value.Request.AccountId && saved.OperationKey == confirmation.OperationKey &&
                saved.Shops is [var target] && target.ShopId == shop &&
                saved.FinancialTreatment == ShopClosuresV1.FinancialTreatment, ct);
    }

    private Task<ErrorOr<T>> ReadClosureAsync<T>(MerchantBillingCaller caller, BillingDelegationIntent intent,
        Func<T, bool> valid, CancellationToken ct) where T : class =>
        RunAsync<T>(caller, intent, ShopClosuresV1.BodyLimit, async (response, token) =>
        {
            var result = await ReadJsonAsync<T>(response, token);
            return result is not null && valid(result) ? result : Unavailable("InvalidClosureContract");
        }, ct);
}
