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
        return Cache.GetOrAdd(zoneId, static id => Lookup(id, TimeZoneInfo.FindSystemTimeZoneById));
    }

    /// <summary>
    /// Finds a zone through <paramref name="find"/>. A host without tz data still has to serve the
    /// default zone, so only that one falls back to its fixed UTC+05:30 offset (India keeps no DST);
    /// any other unknown id keeps throwing.
    /// </summary>
    internal static TimeZoneInfo Lookup(string zoneId, Func<string, TimeZoneInfo> find)
    {
        try
        {
            return find(zoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException
            && string.Equals(zoneId, DefaultId, StringComparison.Ordinal))
        {
            return TimeZoneInfo.CreateCustomTimeZone(DefaultId, TimeSpan.FromMinutes(330), DefaultId, "India Standard Time");
        }
    }
}
