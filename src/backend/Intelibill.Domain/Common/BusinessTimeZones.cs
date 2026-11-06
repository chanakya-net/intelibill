using System.Collections.Concurrent;

namespace Intelibill.Domain.Common;

/// <summary>The IANA zones a shop can keep its business dates in.</summary>
public static class BusinessTimeZones
{
    public const string DefaultId = "Asia/Kolkata";

    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Cache = new(StringComparer.Ordinal);

    /// <summary>Resolves an IANA zone id; throws <see cref="TimeZoneNotFoundException"/> for an unknown one.</summary>
    public static TimeZoneInfo Resolve(string zoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        return Cache.GetOrAdd(zoneId, static id => TimeZoneInfo.FindSystemTimeZoneById(id));
    }
}
