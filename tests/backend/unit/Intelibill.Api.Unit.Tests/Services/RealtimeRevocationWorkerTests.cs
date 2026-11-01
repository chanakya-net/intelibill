using Intelibill.Api.Hubs;
using Intelibill.Api.Services.Realtime;
using Intelibill.Api.Unit.Tests.Hubs;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Api.Unit.Tests.Services;

/// <summary>
/// The sweep closes sockets whose authority has gone. It is not what makes delivery safe — the
/// dispatcher is — so its failure mode is a socket that stays open a little longer, never a payload
/// that gets through.
/// </summary>
public class RealtimeRevocationWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IRealtimeConnectionRegistry _registry = Substitute.For<IRealtimeConnectionRegistry>();
    private readonly FakeSessionAuthority _authority = new();
    private readonly RealtimeConnectionTracker _tracker = new();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly RealtimeInstanceIdentity _instance = new(RealtimeTestHarness.InstanceId);

    public RealtimeRevocationWorkerTests() =>
        _registry
            .ListInstanceConnectionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<RealtimeConnection>>([]));

    [Fact]
    public async Task Sweep_ClosesTheSocketOfARemovedMembership()
    {
        var connection = Lease("connection-1");
        _authority.LiveSessions.Add(connection.SessionId);
        Held(connection);

        var closed = await CreateWorker().SweepAsync(CancellationToken.None);

        Assert.Equal(1, closed);
        Assert.False(_tracker.TryGet("connection-1", out _));
        await _registry.Received(1).RemoveAsync("connection-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_ClosesTheSocketOfARevokedSession()
    {
        var connection = Lease("connection-1");
        _authority.LiveMemberships.Add((connection.UserId, connection.ShopId, connection.ShopRole!));
        Held(connection);

        var closed = await CreateWorker().SweepAsync(CancellationToken.None);

        Assert.Equal(1, closed);
        await _registry.Received(1).RemoveAsync("connection-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_ClosesTheSocketOfAnExpiredToken()
    {
        // CloseOnAuthenticationExpiration handles this in the transport; the sweep is the backstop
        // for a connection that somehow outlived it, so the two do not depend on each other.
        var connection = Lease("connection-1") with { ExpiresAt = Now };
        MakeLive(connection);
        Held(connection);

        var closed = await CreateWorker().SweepAsync(CancellationToken.None);

        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Sweep_LeavesALiveConnectionAlone()
    {
        var connection = Lease("connection-1");
        MakeLive(connection);
        Held(connection);

        var closed = await CreateWorker().SweepAsync(CancellationToken.None);

        Assert.Equal(0, closed);
        Assert.True(_tracker.TryGet("connection-1", out _));
        await _registry.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task Sweep_DeletesExpiredLeases()
    {
        await CreateWorker().SweepAsync(CancellationToken.None);

        await _registry.Received(1).RemoveExpiredAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_ReadsOnlyTheLeasesThisProcessHolds()
    {
        // Another replica's socket cannot be closed from here. Claiming its lease would delete the
        // row the replica that owns it needs in order to find and close it.
        await CreateWorker().SweepAsync(CancellationToken.None);

        await _registry.Received(1).ListInstanceConnectionsAsync(
            RealtimeTestHarness.InstanceId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_WhenAuthorityIsUnreachable_LeavesTheSocketOpen()
    {
        // Failing closed here would turn a transient database blip into a disconnect storm, and it
        // would buy nothing: the dispatcher already refuses to deliver while the store is down.
        var connection = Lease("connection-1");
        MakeLive(connection);
        Held(connection);
        _authority.Failure = new InvalidOperationException("authority store unavailable");

        var closed = await CreateWorker().SweepAsync(CancellationToken.None);

        Assert.Equal(0, closed);
        Assert.True(_tracker.TryGet("connection-1", out _));
        await _registry.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task Sweep_WhenCleanupFails_Propagates()
    {
        // The background loop catches and retries on the next tick; swallowing it here would hide a
        // registry that has stopped working entirely.
        _registry
            .RemoveExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("registry unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateWorker().SweepAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Sweep_RemovesTheLeaseOfASocketThisProcessNoLongerHolds()
    {
        // A lease left behind by a socket that closed without a clean disconnect. There is nothing
        // to abort, but the row must not keep appearing as a candidate recipient.
        var connection = Lease("connection-1");
        _registry
            .ListInstanceConnectionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<RealtimeConnection>>([connection]));

        var closed = await CreateWorker().SweepAsync(CancellationToken.None);

        Assert.Equal(0, closed);
        await _registry.Received(1).RemoveAsync("connection-1", Arg.Any<CancellationToken>());
    }

    private RealtimeRevocationWorker CreateWorker()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_registry);
        services.AddSingleton<ILiveSessionAuthority>(_authority);
        var provider = services.BuildServiceProvider();

        return new RealtimeRevocationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _tracker,
            _instance,
            _clock,
            NullLogger<RealtimeRevocationWorker>.Instance);
    }

    private static RealtimeConnection Lease(string connectionId) =>
        new(connectionId,
            RealtimeTestHarness.InstanceId,
            nameof(ProductHub),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Owner",
            SecurityVersion: 3,
            ScopeVersion: 1,
            ExpiresAt: Now.AddMinutes(10));

    private void MakeLive(RealtimeConnection connection)
    {
        _authority.LiveSessions.Add(connection.SessionId);
        _authority.LiveMemberships.Add((connection.UserId, connection.ShopId, connection.ShopRole!));
    }

    /// <summary>Puts the connection in both places a live socket appears: the registry and the tracker.</summary>
    private void Held(RealtimeConnection connection)
    {
        _registry
            .ListInstanceConnectionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<RealtimeConnection>>([connection]));
        _tracker.Track(connection, RealtimeTestHarness.CallerContext(connection.ConnectionId, claims: null));
    }
}
