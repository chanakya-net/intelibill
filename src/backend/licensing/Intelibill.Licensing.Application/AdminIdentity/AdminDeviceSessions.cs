using System.Globalization;
using ErrorOr;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

public sealed record AdminSessionCaller(Guid AdminId, Guid SessionId, DateTimeOffset AccessExpiresAt);
public sealed record AdminDeviceSession(Guid Id, bool IsCurrent, DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt, DateTimeOffset AbsoluteExpiresAt);
public sealed record AdminDeviceSessionPage(IReadOnlyList<AdminDeviceSession> Items, string? NextCursor);
public sealed record AdminDeviceSessionSeek(DateTimeOffset CreatedAt, Guid Id)
{
    internal static AdminDeviceSessionSeek? Parse(string cursor)
    {
        if (cursor.Length is < 34 or > 52) return null;
        var parts = cursor.Split('.');
        return parts.Length == 2 && Guid.TryParseExact(parts[1], "N", out var id) && id != Guid.Empty
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            && ticks <= DateTimeOffset.MaxValue.Ticks && ticks % 10 == 0
            ? new(new DateTimeOffset(ticks, TimeSpan.Zero), id) : null;
    }

    public override string ToString() => FormattableString.Invariant($"{CreatedAt.UtcTicks}.{Id:N}");
}

public sealed class AdminDeviceSessions(IAdminSessionStore sessions)
{
    public Task<ErrorOr<AdminDeviceSessionPage>> ListAsync(AdminSessionCaller caller, string? size, string? cursor,
        CancellationToken token)
    {
        var pageSize = 25;
        if (size is not null && (!int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
            || pageSize is < 1 or > 100))
            return Task.FromResult<ErrorOr<AdminDeviceSessionPage>>(InvalidQuery);
        var seek = cursor is null ? null : AdminDeviceSessionSeek.Parse(cursor);
        return cursor is not null && seek is null
            ? Task.FromResult<ErrorOr<AdminDeviceSessionPage>>(InvalidQuery)
            : sessions.ListDevicesAsync(caller, pageSize, seek, token);
    }

    public Task<ErrorOr<Success>> RevokeAsync(AdminSessionCaller caller, Guid targetId, string? reason,
        CancellationToken token)
    {
        AuditReason? code = reason switch
        {
            "device-unused" => AuditReason.DeviceUnused,
            "device-unrecognized" => AuditReason.DeviceUnrecognized,
            _ => null,
        };
        return code is { } selected ? sessions.RevokeDeviceAsync(caller, targetId, selected, token)
            : Task.FromResult<ErrorOr<Success>>(Error.Validation("Admin.SessionReasonRequired", "Select a device revocation reason."));
    }

    public static Error InvalidQuery => Error.Validation("Admin.SessionQuery", "Use a valid page size and cursor for your own sessions.");
    public static Error TargetUnavailable => Error.NotFound("Admin.SessionNotFound", "The device session was not found.");
    public static Error RecentMfaRequired => Error.Forbidden("Admin.RecentMfaRequired", "Sign in again with your authenticator to revoke another device.");
}
