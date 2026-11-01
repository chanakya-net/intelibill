using Intelibill.Api.Hubs;
using Intelibill.Api.Services.Realtime;
using Intelibill.Api.Unit.Tests.Hubs;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Intelibill.Api.Unit.Tests.Services;

/// <summary>
/// What replaced the group broadcast. A shop event is addressed to connection ids that were
/// re-authorized for this payload, so a transport whose authority lapsed after it connected is not
/// a recipient — and a lookup that cannot answer sends nothing rather than falling back to the
/// whole group.
/// </summary>
public class AuthorizedRealtimeDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IHubContext<ProductHub> _hubContext = Substitute.For<IHubContext<ProductHub>>();
    private readonly IHubClients _hubClients = Substitute.For<IHubClients>();
    private readonly IClientProxy _targeted = Substitute.For<IClientProxy>();
    private readonly IRealtimeConnectionRegistry _registry = Substitute.For<IRealtimeConnectionRegistry>();
    private readonly FakeSessionAuthority _authority = new();
    private readonly FixedTimeProvider _clock = new(Now);

    private readonly Guid _shopId = Guid.NewGuid();

    public AuthorizedRealtimeDispatcherTests()
    {
        _hubContext.Clients.Returns(_hubClients);
        _hubClients.Clients(Arg.Any<IReadOnlyList<string>>()).Returns(_targeted);
        _registry
            .ListShopConnectionsAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<RealtimeConnection>>([]));
    }

    [Fact]
    public async Task SendShopAsync_SendsOnlyToConnectionsThatStillValidate()
    {
        var live = LeaseFor("connection-live");
        var lapsed = LeaseFor("connection-lapsed");
        MakeLive(live);
        // lapsed: its session is live but the membership behind it is gone.
        _authority.LiveSessions.Add(lapsed.SessionId);
        Candidates(live, lapsed);

        await CreateDispatcher().SendShopAsync(_shopId, "ProductAdded", new { ok = true });

        _hubClients.Received(1).Clients(Arg.Is<IReadOnlyList<string>>(ids =>
            ids.Count == 1 && ids[0] == "connection-live"));
        await _targeted.Received(1).SendCoreAsync("ProductAdded", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendShopAsync_ResolvesCandidatesForThisHubAndShopOnly()
    {
        var live = LeaseFor("connection-1");
        MakeLive(live);
        Candidates(live);

        await CreateDispatcher().SendShopAsync(_shopId, "ProductAdded", new { ok = true });

        await _registry.Received(1).ListShopConnectionsAsync(
            nameof(ProductHub),
            _shopId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendShopAsync_SkipsLeasesWhosePresentedTokenHasExpired()
    {
        // Expiry needs no lookup to be decisive, and a lease the sweep has not reached yet must not
        // become a recipient just because the authority row behind it is still fine.
        var expired = LeaseFor("connection-expired") with { ExpiresAt = Now };
        MakeLive(expired);
        Candidates(expired);

        await CreateDispatcher().SendShopAsync(_shopId, "ProductAdded", new { ok = true });

        AssertNothingSent();
    }

    [Fact]
    public async Task MembershipRevoked_AfterCommit_NoNewAuthorizedDispatch()
    {
        // The barrier: the lease is read, then the revocation commits, then authorization runs. A
        // payload authorized before that commit may already be in transit and cannot be recalled —
        // this asserts the other side of the line, that no NEW delivery decision comes out yes.
        var lease = LeaseFor("connection-1");
        MakeLive(lease);
        _registry
            .ListShopConnectionsAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _authority.LiveMemberships.Remove((lease.UserId, lease.ShopId, lease.ShopRole!));
                return Task.FromResult<IReadOnlyList<RealtimeConnection>>([lease]);
            });

        await CreateDispatcher().SendShopAsync(_shopId, "ProductAdded", new { ok = true });

        AssertNothingSent();
    }

    [Fact]
    public async Task SessionRevoked_AfterDispatch_DoesNotRecallWhatWasAlreadyAuthorized()
    {
        // The complement of the barrier test, stated so the boundary is not mistaken for a promise
        // to unsend: a payload authorized while access was live is delivered.
        var lease = LeaseFor("connection-1");
        MakeLive(lease);
        Candidates(lease);
        var dispatcher = CreateDispatcher();

        await dispatcher.SendShopAsync(_shopId, "ProductAdded", new { ok = true });
        _authority.LiveSessions.Remove(lease.SessionId);

        await _targeted.Received(1).SendCoreAsync("ProductAdded", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendShopAsync_WhenRegistryLookupFails_SendsNothing()
    {
        _registry
            .ListShopConnectionsAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("registry unavailable"));

        await CreateDispatcher().SendShopAsync(_shopId, "ProductAdded", new { ok = true });

        AssertNothingSent();
        _hubClients.DidNotReceiveWithAnyArgs().Group(default!);
    }

    [Fact]
    public async Task SendShopAsync_WhenAuthorityIsUnreachable_SendsNothing()
    {
        var lease = LeaseFor("connection-1");
        MakeLive(lease);
        Candidates(lease);
        _authority.Failure = new InvalidOperationException("authority store unavailable");

        await CreateDispatcher().SendShopAsync(_shopId, "ProductAdded", new { ok = true });

        AssertNothingSent();
        _hubClients.DidNotReceiveWithAnyArgs().Group(default!);
    }

    [Fact]
    public async Task SendShopAsync_WithNoLiveConnections_SendsNothing()
    {
        await CreateDispatcher().SendShopAsync(_shopId, "ProductAdded", new { ok = true });

        AssertNothingSent();
    }

    [Fact]
    public async Task SendShopAsync_ChecksEachDistinctAuthorityOnce()
    {
        // One device with several tabs is several connections under one session. Re-asking the same
        // question per tab would multiply the authority reads a single mutation costs.
        var first = LeaseFor("connection-1");
        var second = first with { ConnectionId = "connection-2" };
        var otherSession = LeaseFor("connection-3");
        MakeLive(first);
        MakeLive(otherSession);
        Candidates(first, second, otherSession);
        var counting = new CountingAuthority(_authority);

        await CreateDispatcher(counting).SendShopAsync(_shopId, "ProductAdded", new { ok = true });

        Assert.Equal(2, counting.Calls);
        _hubClients.Received(1).Clients(Arg.Is<IReadOnlyList<string>>(ids => ids.Count == 3));
    }

    [Fact]
    public async Task SendShopAsync_ForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var lease = LeaseFor("connection-1");
        MakeLive(lease);
        Candidates(lease);

        await CreateDispatcher().SendShopAsync(_shopId, "ProductAdded", new { ok = true }, cts.Token);

        await _targeted.Received(1).SendCoreAsync("ProductAdded", Arg.Any<object?[]>(), cts.Token);
    }

    private void AssertNothingSent()
    {
        _hubClients.DidNotReceiveWithAnyArgs().Clients(default!);
        _targeted.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
    }

    private AuthorizedRealtimeDispatcher<ProductHub> CreateDispatcher(ILiveSessionAuthority? authority = null) =>
        new(_hubContext,
            _registry,
            authority ?? _authority,
            _clock,
            NullLogger<AuthorizedRealtimeDispatcher<ProductHub>>.Instance);

    private RealtimeConnection LeaseFor(string connectionId) =>
        new(connectionId,
            RealtimeTestHarness.InstanceId,
            nameof(ProductHub),
            Guid.NewGuid(),
            Guid.NewGuid(),
            _shopId,
            "Owner",
            SecurityVersion: 3,
            ScopeVersion: 1,
            ExpiresAt: Now.AddMinutes(10));

    private void MakeLive(RealtimeConnection connection)
    {
        _authority.LiveSessions.Add(connection.SessionId);
        _authority.LiveMemberships.Add((connection.UserId, connection.ShopId, connection.ShopRole!));
    }

    private void Candidates(params RealtimeConnection[] connections) =>
        _registry
            .ListShopConnectionsAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<RealtimeConnection>>(connections));

    private sealed class CountingAuthority(ILiveSessionAuthority inner) : ILiveSessionAuthority
    {
        public int Calls { get; private set; }

        public Task<bool> IsValidAsync(
            Intelibill.Application.Common.Security.SessionClaims claims,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.IsValidAsync(claims, cancellationToken);
        }
    }
}
