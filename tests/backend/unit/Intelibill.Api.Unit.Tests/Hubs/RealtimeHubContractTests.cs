using Intelibill.Api.Services.Realtime;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Api.Unit.Tests.Hubs;

/// <summary>
/// The admission contract both notification hubs owe. A signed token is not a subscription: the
/// session, the account and the membership it names are read live before anything is registered,
/// and nothing is registered for a connection that is then aborted.
/// </summary>
public abstract class RealtimeHubContractTests<THub>
    where THub : Hub
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeSessionAuthority _authority = new();
    private readonly IRealtimeConnectionRegistry _registry = Substitute.For<IRealtimeConnectionRegistry>();
    private readonly RealtimeConnectionTracker _tracker = new();
    private readonly FixedTimeProvider _clock = new(Now);

    protected abstract THub CreateHub(RealtimeConnectionGate gate);

    protected abstract string ExpectedHubName { get; }

    [Fact]
    public async Task Connect_WithLiveSessionAndMembership_RegistersConnection()
    {
        var session = RealtimeTestSession.Create(Now);
        session.MakeFullyLive(_authority);
        var (hub, context) = Connect("connection-1", session);

        await hub.OnConnectedAsync();

        await _registry.Received(1).RegisterAsync(
            Arg.Is<RealtimeConnection>(c =>
                c.ConnectionId == "connection-1"
                && c.InstanceId == RealtimeTestHarness.InstanceId
                && c.HubName == ExpectedHubName
                && c.UserId == session.UserId
                && c.SessionId == session.SessionId
                && c.ShopId == session.ShopId
                && c.ShopRole == session.Role
                && c.SecurityVersion == session.SecurityVersion
                && c.ScopeVersion == session.ScopeVersion
                && c.ExpiresAt == session.ExpiresAt),
            Arg.Any<CancellationToken>());
        context.DidNotReceive().Abort();
        Assert.True(_tracker.TryGet("connection-1", out _));
    }

    [Fact]
    public async Task Connect_RemovedMembership_IsAborted()
    {
        // The device's session is untouched — only the membership that gave it a shop is gone, which
        // is exactly the state a removed employee's still-signed token is in.
        var session = RealtimeTestSession.Create(Now);
        _authority.LiveSessions.Add(session.SessionId);
        var (hub, context) = Connect("connection-2", session);

        await hub.OnConnectedAsync();

        await AssertNothingRegistered();
        context.Received(1).Abort();
        Assert.False(_tracker.TryGet("connection-2", out _));
    }

    [Fact]
    public async Task Connect_RevokedSession_IsAborted()
    {
        // The reverse: the account still belongs to the shop, but this device was signed out.
        var session = RealtimeTestSession.Create(Now);
        _authority.LiveMemberships.Add((session.UserId, session.ShopId, session.Role));
        var (hub, context) = Connect("connection-3", session);

        await hub.OnConnectedAsync();

        await AssertNothingRegistered();
        context.Received(1).Abort();
    }

    [Fact]
    public async Task Connect_WithExpiredToken_IsAborted()
    {
        var session = RealtimeTestSession.Create(Now);
        session.MakeFullyLive(_authority);
        _clock.Now = session.ExpiresAt;
        var (hub, context) = Connect("connection-4", session);

        await hub.OnConnectedAsync();

        await AssertNothingRegistered();
        context.Received(1).Abort();
    }

    [Fact]
    public async Task Connect_WithoutSessionClaim_IsAborted()
    {
        var session = RealtimeTestSession.Create(Now);
        session.MakeFullyLive(_authority);
        var claims = session.ToClaims().Where(c => c.Type != "sid");
        var (hub, context) = Connect("connection-5", claims);

        await hub.OnConnectedAsync();

        await AssertNothingRegistered();
        context.Received(1).Abort();
    }

    [Fact]
    public async Task Connect_WithUnparseableSessionClaim_IsAborted()
    {
        var session = RealtimeTestSession.Create(Now);
        session.MakeFullyLive(_authority);
        var claims = session.ToClaims()
            .Select(c => c.Type == "sid" ? new System.Security.Claims.Claim("sid", "not-a-guid") : c);
        var (hub, context) = Connect("connection-6", claims);

        await hub.OnConnectedAsync();

        await AssertNothingRegistered();
        context.Received(1).Abort();
    }

    [Fact]
    public async Task Connect_WithoutActiveShopClaim_IsAborted()
    {
        var session = RealtimeTestSession.Create(Now);
        session.MakeFullyLive(_authority);
        var claims = session.ToClaims().Where(c => c.Type != "active_shop_id");
        var (hub, context) = Connect("connection-7", claims);

        await hub.OnConnectedAsync();

        await AssertNothingRegistered();
        context.Received(1).Abort();
    }

    [Fact]
    public async Task Connect_WithUnparseableActiveShopClaim_IsAborted()
    {
        var session = RealtimeTestSession.Create(Now);
        session.MakeFullyLive(_authority);
        var claims = session.ToClaims()
            .Select(c => c.Type == "active_shop_id"
                ? new System.Security.Claims.Claim("active_shop_id", "not-a-guid")
                : c);
        var (hub, context) = Connect("connection-8", claims);

        await hub.OnConnectedAsync();

        await AssertNothingRegistered();
        context.Received(1).Abort();
    }

    [Fact]
    public async Task Connect_WithoutAnyClaims_IsAborted()
    {
        var (hub, context) = Connect("connection-9", claims: null);

        await hub.OnConnectedAsync();

        await AssertNothingRegistered();
        context.Received(1).Abort();
    }

    [Fact]
    public async Task Connect_WhenAuthorityIsUnreachable_IsAborted()
    {
        // Fail closed: a store that cannot answer "is this still allowed" is not an answer of yes.
        var session = RealtimeTestSession.Create(Now);
        session.MakeFullyLive(_authority);
        _authority.Failure = new InvalidOperationException("authority store unavailable");
        var (hub, context) = Connect("connection-10", session);

        await hub.OnConnectedAsync();

        await AssertNothingRegistered();
        context.Received(1).Abort();
    }

    [Fact]
    public async Task Connect_WhenRegistrationFails_IsAborted()
    {
        // A connection the dispatcher cannot see is a connection that would never be authorized
        // again; admitting it would leave a transport open that no revocation sweep can find.
        var session = RealtimeTestSession.Create(Now);
        session.MakeFullyLive(_authority);
        _registry
            .RegisterAsync(Arg.Any<RealtimeConnection>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("registry unavailable"));
        var (hub, context) = Connect("connection-11", session);

        await hub.OnConnectedAsync();

        context.Received(1).Abort();
        Assert.False(_tracker.TryGet("connection-11", out _));
    }

    [Fact]
    public async Task Disconnect_RemovesRegistrationAndStopsTracking()
    {
        var session = RealtimeTestSession.Create(Now);
        session.MakeFullyLive(_authority);
        var (hub, _) = Connect("connection-12", session);
        await hub.OnConnectedAsync();

        await hub.OnDisconnectedAsync(exception: null);

        await _registry.Received(1).RemoveAsync("connection-12", Arg.Any<CancellationToken>());
        Assert.False(_tracker.TryGet("connection-12", out _));
    }

    private async Task AssertNothingRegistered()
    {
        await _registry.DidNotReceiveWithAnyArgs().RegisterAsync(default!, default);
    }

    private (THub Hub, HubCallerContext Context) Connect(string connectionId, RealtimeTestSession session) =>
        Connect(connectionId, session.ToClaims());

    private (THub Hub, HubCallerContext Context) Connect(
        string connectionId,
        IEnumerable<System.Security.Claims.Claim>? claims)
    {
        var context = RealtimeTestHarness.CallerContext(connectionId, claims);
        var gate = new RealtimeConnectionGate(
            _authority,
            _registry,
            _tracker,
            new RealtimeInstanceIdentity(RealtimeTestHarness.InstanceId),
            _clock,
            NullLogger<RealtimeConnectionGate>.Instance);

        var hub = CreateHub(gate);
        hub.Context = context;
        return (hub, context);
    }
}
