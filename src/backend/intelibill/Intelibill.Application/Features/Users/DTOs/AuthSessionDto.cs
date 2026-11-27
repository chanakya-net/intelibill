namespace Intelibill.Application.Features.Users.DTOs;

/// <summary>
/// One of the owner's own devices, as shown to them. The label and user agent are there so they can
/// recognise the device; neither authorises anything.
/// </summary>
public sealed record AuthSessionDto(
    Guid SessionId,
    string? DeviceLabel,
    string? UserAgent,
    string? IpAddress,
    Guid? ActiveShopId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);
