using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Audit;

public sealed partial record AdminAuditFilters(BootstrapAuditAction? Action, DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc, int PageSize, DateTimeOffset? BeforeTime = null, Guid? BeforeId = null)
{
    internal static AdminAuditFilters? Parse(string? action, string? from, string? to, string? size, string? cursor)
    {
        var pageSize = 25;
        if (size is not null && (!int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
            || pageSize is < 1 or > AdminAuditQuery.MaxPageSize))
            return null;

        var actionEntry = action is null ? null : AdminAuditActions.Find(action);
        if ((action is not null && actionEntry is null) || !TryReadTime(from, out var start)
            || !TryReadTime(to, out var end) || start >= end)
            return null;

        var filters = new AdminAuditFilters(actionEntry?.Id, start, end, pageSize);
        return cursor is null ? filters : filters.DecodeCursor(cursor);
    }

    internal string EncodeCursor(DateTimeOffset time, Guid id) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(
            FormattableString.Invariant($"1|{time.UtcTicks}|{id:N}|{Fingerprint()}")));

    private AdminAuditFilters? DecodeCursor(string value)
    {
        if (value.Length is < 1 or > 160 || !Base64Url.IsValid(value)) return null;
        var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(value)).Split('|');
        if (parts.Length != 4 || parts[0] != "1" || parts[3] != Fingerprint()
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.Ticks || ticks % 10 != 0
            || !Guid.TryParseExact(parts[2], "N", out var id))
            return null;

        var time = new DateTimeOffset(ticks, TimeSpan.Zero);
        return time < FromUtc || time >= ToUtc ? null : this with { BeforeTime = time, BeforeId = id };
    }

    private string Fingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(
        FormattableString.Invariant($"{AdminAuditQuery.IdentityScope}|{(int?)Action}|{FromUtc?.UtcTicks}|{ToUtc?.UtcTicks}|{PageSize}"))));

    private static bool TryReadTime(string? value, out DateTimeOffset? time)
    {
        time = null;
        if (value is null) return true;
        if (value.Length is < 20 or > 32 || !UtcFilterPattern().IsMatch(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        time = parsed.ToUniversalTime();
        return true;
    }

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,6})?(Z|[+-][0-9]{2}:[0-9]{2})\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex UtcFilterPattern();
}
