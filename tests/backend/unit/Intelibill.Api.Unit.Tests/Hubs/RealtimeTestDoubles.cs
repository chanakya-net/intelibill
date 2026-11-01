using System.Security.Claims;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Realtime;
using Intelibill.Application.Common.Security;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Hubs;

/// <summary>
/// Stands in for <c>LiveSessionValidator</c> by holding the two facts it reads separately, so a
/// test can express "the session is gone" and "the membership is gone" as different arrangements
/// instead of two spellings of the same boolean.
/// </summary>
internal sealed class FakeSessionAuthority : ILiveSessionAuthority
{
    public HashSet<Guid> LiveSessions { get; } = [];

    public HashSet<(Guid UserId, Guid ShopId, string Role)> LiveMemberships { get; } = [];

    /// <summary>Set to make the authority store unreachable rather than negative.</summary>
    public Exception? Failure { get; set; }

    /// <summary>How many questions are answered before <see cref="Failure"/> starts being thrown.</summary>
    public int FailAfter { get; set; }

    public int Calls { get; private set; }

    public Task<bool> IsValidAsync(SessionClaims claims, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claims);
        Calls++;

        if (Failure is not null && Calls > FailAfter)
            return Task.FromException<bool>(Failure);

        if (!LiveSessions.Contains(claims.SessionId))
            return Task.FromResult(false);

        if (claims.ActiveShopId is null)
            return Task.FromResult(true);

        return Task.FromResult(
            LiveMemberships.Contains((claims.UserId, claims.ActiveShopId.Value, claims.ActiveShopRole ?? string.Empty)));
    }
}

/// <summary>
/// The authority state a connecting device needs, plus the claims that describe it. Tests mutate
/// the authority before connecting to model removal, revocation or expiry.
/// </summary>
internal sealed record RealtimeTestSession(
    Guid UserId,
    Guid SessionId,
    Guid ShopId,
    string Role,
    long SecurityVersion,
    long ScopeVersion,
    DateTimeOffset ExpiresAt)
{
    public static RealtimeTestSession Create(DateTimeOffset now) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Owner", 3, 1, now.AddMinutes(15));

    public Claim[] ToClaims() =>
    [
        new(ClaimTypes.NameIdentifier, UserId.ToString()),
        new("sid", SessionId.ToString()),
        new("security_version", SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("scope_version", ScopeVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("active_shop_id", ShopId.ToString()),
        new("active_shop_role", Role),
        new("exp", ExpiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
    ];

    public void MakeFullyLive(FakeSessionAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        authority.LiveSessions.Add(SessionId);
        authority.LiveMemberships.Add((UserId, ShopId, Role));
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class RealtimeTestHarness
{
    public const string InstanceId = "test-instance";

    public static HubCallerContext CallerContext(string connectionId, IEnumerable<Claim>? claims)
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(connectionId);
        context.User.Returns(claims is null ? null : new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
        return context;
    }

    public static Task<IReadOnlyList<RealtimeConnection>> Empty =>
        Task.FromResult<IReadOnlyList<RealtimeConnection>>([]);
}
