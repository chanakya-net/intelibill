using System.Security.Cryptography;
using System.Text;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// What the merchant service tells licensing about a device's offline permission: that its own server
/// issued a permit, or that its own server received an offline upload. Every instant is the merchant
/// server's clock. Nothing here describes an invoice, a customer, stock, a user or the device's own state.
/// </summary>
public static class OfflineDeviceObservationsV1
{
    public const int SchemaVersion = 1;
    public const string Path = "/internal/licensing/v1/merchant-events/device-observation";
    public const string Kind = "DeviceObservation";
    public const string PermitIssued = "PermitIssued";
    public const string OfflineSync = "OfflineSync";
    public const string LicenceEnd = "LicenceEnd";
    public const string VerificationFreshness = "VerificationFreshness";
    public const string MerchantLimit = "MerchantLimit";

    /// <summary>Opaque per-shop reference. The client's own device identifier never leaves the merchant service.</summary>
    public static string DeviceRef(Guid shopId, string deviceId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{shopId:D}\n{deviceId}")));

    /// <summary>Coarse category of the bound that ended the permit; merchant-side detail stays behind.</summary>
    public static string Limit(string limitingReason) => limitingReason switch
    {
        "LicenceEnd" or "CurrentLicenceEnd" => LicenceEnd,
        "AuthorityFreshness" or "DeviceFreshness" => VerificationFreshness,
        _ => MerchantLimit,
    };

    public static DateTimeOffset Instant(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        return new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
    }
}

public sealed record OfflineDevicePermitWindowV1(
    Guid PermitId, DateTimeOffset NotBefore, DateTimeOffset ValidUntil, string Limit,
    long AccountRevision, long ShopRevision, DateTimeOffset AuthorityVerifiedAt)
{
    public bool IsValid() => PermitId != Guid.Empty && EntitlementSnapshotsV1.IsInstant(NotBefore) &&
        EntitlementSnapshotsV1.IsInstant(ValidUntil) && NotBefore < ValidUntil &&
        Limit is OfflineDeviceObservationsV1.LicenceEnd or OfflineDeviceObservationsV1.VerificationFreshness
            or OfflineDeviceObservationsV1.MerchantLimit &&
        AccountRevision > 0 && ShopRevision > 0 && EntitlementSnapshotsV1.IsInstant(AuthorityVerifiedAt);
}

public sealed record OfflineDeviceObservationV1(
    int SchemaVersion, Guid EventId, Guid AccountId, Guid ShopId, Guid AffiliationRequestId, string DeviceRef,
    string Observation, DateTimeOffset ObservedAt, OfflineDevicePermitWindowV1? Permit)
{
    /// <summary>Structural only: the two services keep independent clocks, so no instant is compared with "now".</summary>
    public bool IsValid() => SchemaVersion == OfflineDeviceObservationsV1.SchemaVersion && EventId != Guid.Empty &&
        AccountId != Guid.Empty && ShopId != Guid.Empty && AffiliationRequestId != Guid.Empty &&
        EntitlementSnapshotsV1.IsDigest(DeviceRef) && EntitlementSnapshotsV1.IsInstant(ObservedAt) && Observation switch
        {
            OfflineDeviceObservationsV1.PermitIssued => Permit is { } window && window.IsValid() && ObservedAt == window.NotBefore,
            OfflineDeviceObservationsV1.OfflineSync => Permit is null,
            _ => false,
        };
}
