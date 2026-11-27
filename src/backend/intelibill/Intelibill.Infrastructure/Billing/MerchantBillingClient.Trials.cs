using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public async Task<ErrorOr<ShopTrialPreviewV1>> PreviewTrialAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct)
    {
        var before = await trials.CountMembersAsync(caller, shop, ct);
        if (before.IsError)
            return before.Errors;

        var preview = await RunAsync<ShopTrialPreviewV1>(caller,
            new(ShopTrialsV1.PreviewAction, HttpMethod.Get, ShopTrialsV1.PreviewPath(shop), null, []),
            ShopTrialsV1.MaximumBodyBytes, async (response, token) =>
                await ReadJsonAsync<ShopTrialPreviewV1>(response, token) is { } result && IsTrialPreview(result, shop)
                    ? result : Unavailable("InvalidTrialPreview"), ct);
        if (preview.IsError)
            return preview.Errors;
        var members = await trials.CountMembersAsync(caller, shop, ct);
        if (members.IsError)
            return members.Errors;
        var value = preview.Value with { ActiveMembers = members.Value };
        return value.Eligible && members.Value > value.Policy!.MemberCap
            ? value with { Eligible = false, Reason = "MemberCap" } : value;
    }

    private static bool IsTrialPreview(ShopTrialPreviewV1 result, Guid shop) =>
        result.ShopId == shop && result.EvaluatedAt != default &&
        result.ProspectiveEnd - result.EvaluatedAt == TimeSpan.FromDays(ShopTrialsV1.DurationDays) &&
        (result.Policy is null ? !result.Eligible : result.Policy is
            { MemberCap: > 0, AccountTrialCap: > 0, PolicyRevision: > 0, TermsVersion.Length: > 0 and <= 80 }
            && result.Policy.PolicyId != Guid.Empty);

    public Task<ErrorOr<ShopTrialOperationV1>> AcceptTrialAsync(MerchantBillingCaller caller, Guid shop,
        ShopTrialAcceptanceV1? request, CancellationToken ct) =>
        trials.AcceptAsync(caller, shop, request, ct);

    public Task<ErrorOr<ShopTrialOperationV1>> ReadTrialAsync(MerchantBillingCaller caller, Guid shop, Guid operation,
        CancellationToken ct) => trials.ReadAsync(caller, shop, operation, ct);
}
