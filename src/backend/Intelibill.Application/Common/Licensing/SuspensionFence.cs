using ErrorOr;

namespace Intelibill.Application.Common.Licensing;

/// <summary>Applies enrolled suspension rules under the same lock used by projection delivery.</summary>
public static class SuspensionFence
{
    public static async Task<Error?> ReadDenialAsync(this IShopLicenceFence fence, Guid shop, LicenceAction action,
        CancellationToken ct)
    {
        var lease = await fence.EnterAsync(shop, action, ct);
        if (lease.IsError) return lease.FirstError;
        return lease.Value.SuspensionDenial();
    }

    public static Error? SuspensionDenial(this ShopLicenceLease lease) =>
        lease.Decision.Enforced && lease.EffectiveSuspension is not null ? lease.Decision.Denial : null;
}
