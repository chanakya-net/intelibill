using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>Where an asserted issuer stands for one device of one shop.</summary>
internal enum OfflineIssuerStanding
{
    /// <summary>Never held a membership in the shop, in any status.</summary>
    NeverMember = 1,

    /// <summary>Holds or held a membership, but never reserved a lease on the device or synced from it.</summary>
    MemberWithoutDeviceHistory = 2,

    /// <summary>Holds or held a membership and reserved a lease on the device or synced from it.</summary>
    Verified = 3,
}

internal static class OfflineIssuerVerification
{
    /// <summary>
    /// Decision 5: an issuer is verified for a device when they hold or held a membership in the shop
    /// (any status, so a removed cashier still counts) and have history on that device.
    /// </summary>
    internal static async Task<OfflineIssuerStanding> AssessAsync(
        IShopRepository shopRepository,
        IOfflineSubmissionRepository submissionRepository,
        Guid shopId,
        string deviceId,
        Guid issuerUserId,
        CancellationToken cancellationToken)
    {
        var memberships = await shopRepository.GetMembershipsForUsersInShopsAsync([issuerUserId], [shopId], cancellationToken);
        if (memberships.Count == 0)
            return OfflineIssuerStanding.NeverMember;

        return await submissionRepository.HasDeviceHistoryAsync(shopId, deviceId, issuerUserId, cancellationToken)
            ? OfflineIssuerStanding.Verified
            : OfflineIssuerStanding.MemberWithoutDeviceHistory;
    }
}
