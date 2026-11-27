using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Coverage;

/// <summary>What was observed for one device, aggregated over its retained observations.</summary>
public sealed record DevicePermitRow(Guid ShopId, string ShopName, string DeviceRef, DateTimeOffset LastContactAt,
    DateTimeOffset? LastSynchronizedAt, DateTimeOffset? LastPermitIssuedAt, DateTimeOffset? KnownPermitEnd,
    int OutstandingPermits, string? Limit, long? AccountRevision, long? ShopRevision, DateTimeOffset? AuthorityVerifiedAt);

public sealed record DevicePermitPage(IReadOnlyList<DevicePermitRow> Devices, DateTimeOffset? ObservedSince);

public interface IDevicePermitObservationReader
{
    /// <summary>Null when <paramref name="shop"/> is named and is not affiliated with the account now.</summary>
    Task<DevicePermitPage?> ReadAsync(Guid account, Guid? shop, DateTimeOffset evaluatedAt, (Guid Shop, string Device)? after,
        int take, CancellationToken ct);
}

/// <summary>
/// One device as the provider may know it. <c>LastContactAt</c> is the merchant server's latest observation of
/// either kind; <c>LastSynchronizedAt</c> is only ever an offline upload that server received (null when none was
/// observed, never a permit's time); <c>KnownPermitEnd</c> is the latest end among the permits it issued.
/// </summary>
public sealed record DevicePermitStatus(Guid ShopId, string ShopName, string DeviceRef, DateTimeOffset LastContactAt,
    DateTimeOffset? LastSynchronizedAt, DateTimeOffset? LastPermitIssuedAt, bool Outstanding, DateTimeOffset? KnownPermitEnd,
    int OutstandingPermits, string? Limit, long? AccountRevision, long? ShopRevision, DateTimeOffset? AuthorityVerifiedAt);

/// <summary>
/// <c>DisconnectedQueues</c> is always <c>Unknown</c>: invoices a device has issued and not yet uploaded are
/// never visible here. <c>ObservedSince</c> is the account's earliest retained observation (null when there is
/// none): devices that last made contact before reporting began are absent, which proves nothing about them.
/// </summary>
public sealed record AccountDevicePermits(Guid AccountId, DateTimeOffset EvaluatedAt, string DisconnectedQueues,
    DateTimeOffset? ObservedSince, IReadOnlyList<DevicePermitStatus> Devices, string? NextCursor);

/// <summary>
/// Provider read of device contact and outstanding offline permission. A live, MFA-enrolled administrator on a
/// live session needs the persisted Support, Licensing Manager or Security Administrator grant, re-read under
/// lock. Only signed merchant observations are read; no merchant business data exists on this side.
/// </summary>
public sealed class ReadDevicePermitsHandler(ISubscriptionStatusStore store, IDevicePermitObservationReader reader,
    TimeProvider statusClock, TimeProvider sessionClock)
{
    public const int PageSize = 50;
    public const string QueuesUnknown = "Unknown";
    private const AdminCapability Readers =
        AdminCapability.Support | AdminCapability.LicensingManager | AdminCapability.SecurityAdministrator;

    public static Error Denied => Error.Forbidden("Status.AccessDenied",
        "Only Support, Licensing Manager or Security administrators can read device permission.");

    public Task<ErrorOr<AccountDevicePermits>> ReadAsync(AdminSessionCaller caller, Guid account, Guid? shop, string? after,
        CancellationToken ct) => store.InProviderTransactionAsync<AccountDevicePermits>(async token =>
    {
        var cursor = ParseCursor(after);
        if (account == Guid.Empty || shop == Guid.Empty || after is not null && cursor is null)
            return SubscriptionStatusErrors.ShopNotCovered;
        if (await AuthorizeAsync(caller, token) is { } refusal) return refusal;
        await store.UseAccountAsync(account, token);
        var evaluatedAt = SubscriptionStatusProjection.EvaluationInstant(statusClock);
        var page = await reader.ReadAsync(account, shop, evaluatedAt, cursor, PageSize + 1, token);
        if (page is null) return SubscriptionStatusErrors.ShopNotCovered;
        var devices = page.Devices.Take(PageSize).Select(row => Project(row, evaluatedAt)).ToList();
        var next = page.Devices.Count > PageSize ? $"{devices[^1].ShopId:D}.{devices[^1].DeviceRef}" : null;
        return new AccountDevicePermits(account, evaluatedAt, QueuesUnknown, page.ObservedSince, devices, next);
    }, ct);

    /// <summary>Half-open: a permit is outstanding strictly before its end.</summary>
    private static DevicePermitStatus Project(DevicePermitRow row, DateTimeOffset evaluatedAt) =>
        new(row.ShopId, row.ShopName, row.DeviceRef, row.LastContactAt, row.LastSynchronizedAt, row.LastPermitIssuedAt,
            row.KnownPermitEnd is { } end && evaluatedAt < end, row.KnownPermitEnd, row.OutstandingPermits, row.Limit,
            row.AccountRevision, row.ShopRevision, row.AuthorityVerifiedAt);

    private static (Guid Shop, string Device)? ParseCursor(string? after)
    {
        if (after?.Split('.') is not [var shop, { Length: 64 } device] || !Guid.TryParseExact(shop, "D", out var id) ||
            !device.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return null;
        return (id, device);
    }

    /// <summary>Live authority under the identity → session lock order.</summary>
    private async Task<Error?> AuthorizeAsync(AdminSessionCaller caller, CancellationToken ct)
    {
        var (actor, session) = await store.LockProviderAsync(caller, ct);
        var now = sessionClock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now)
            || actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return AdminRefreshErrors.SessionEnded;
        return (actor.Capabilities & Readers) == 0 ? Denied : null;
    }
}
