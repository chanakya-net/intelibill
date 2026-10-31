namespace Intelibill.Domain.ValueObjects;

/// <summary>
/// What the server observed about the device a session was started from, kept so the owner can
/// recognise their own devices in a list. It is description, never authentication: nothing is
/// authorised because a request repeats these values.
/// </summary>
public sealed record DeviceMetadata(string? UserAgent, string? IpAddress)
{
    public const int UserAgentMaxLength = 256;
    public const int IpAddressMaxLength = 64;

    public static DeviceMetadata Unknown { get; } = new(null, null);

    public static DeviceMetadata From(string? userAgent, string? ipAddress) =>
        new(Clip(userAgent, UserAgentMaxLength), Clip(ipAddress, IpAddressMaxLength));

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
