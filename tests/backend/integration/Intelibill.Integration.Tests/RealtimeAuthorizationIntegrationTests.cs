using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Api.Services.Realtime;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The acceptance evidence for the realtime boundary, over a real transport against a real
/// PostgreSQL: a signed token opens a subscription only while the authority behind it is live, a
/// withdrawn membership or a revoked device stops the next payload rather than the next sign-in, and
/// revoking on one replica stops delivery on another without any signal passing between them.
/// </summary>
/// <remarks>
/// What is recorded in assertions and names is connection ids and opaque session ids. The access
/// token travels in the query string on the hub handshake, so it is never written into a message, a
/// log line or a test name here.
/// </remarks>
[Collection("Integration Tests")]
public sealed class RealtimeAuthorizationIntegrationTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string Password = "OwnersPassword!1";

    /// <summary>
    /// Long enough that nothing here depends on a timer firing, short enough that a stuck wait fails
    /// the run instead of hanging it.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    private readonly ApiWebApplicationFactory _factory = new(fixture);

    /// <summary>
    /// The same API with the revocation sweep removed, so a connection is only ever closed or
    /// refused by the dispatcher. Without this, a test asserting "nothing arrived" cannot tell
    /// per-payload authorization from a sweep that happened to tick inside the window.
    /// </summary>
    private readonly ApiWebApplicationFactory _withoutSweep = new(
        fixture,
        services =>
        {
            foreach (var descriptor in services
                .Where(x => x.ImplementationType?.Name == nameof(RealtimeRevocationWorker))
                .ToArray())
            {
                services.Remove(descriptor);
            }
        });

    /// <summary>
    /// A second API process on the same database. Deliberately never initialized: initialization
    /// drops and re-migrates the database, which would take the first instance's data with it. It
    /// shares the fixture's container, so it shares the schema the first instance created.
    /// </summary>
    private readonly ApiWebApplicationFactory _replicaB = new(fixture);

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _withoutSweep.Dispose();
        _replicaB.Dispose();
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task LiveMembership_ReceivesShopEvents()
    {
        using var client = Client(_factory);
        var watcher = await SignInAsync(client);
        var actor = await SecondDeviceAsync(client, watcher);

        await using var hub = await ConnectAsync(_factory, watcher.AccessToken);
        using var received = Watch(hub);

        await CreateItemAsync(client, actor.AccessToken);

        var payload = await received.Received.WaitAsync(Settle);
        Assert.Equal(watcher.ShopId, payload.GetProperty("shopId").GetGuid());
    }

    [Fact]
    public async Task RevokedSession_StopsNewPayloadsAndClosesTheTransport()
    {
        using var client = Client(_factory);
        var watcher = await SignInAsync(client);
        var actor = await SecondDeviceAsync(client, watcher);

        await using var hub = await ConnectAsync(_factory, watcher.AccessToken);
        var closed = ClosedSignal(hub);

        // Baseline first, so a later silence means "refused" and not "never worked".
        using var first = Watch(hub);
        await CreateItemAsync(client, actor.AccessToken);
        await first.Received.WaitAsync(Settle);

        await RevokeSessionAsync(client, actor, watcher.SessionId);

        using var afterRevocation = Watch(hub);
        await CreateItemAsync(client, actor.AccessToken);
        await Assert.ThrowsAsync<TimeoutException>(() => afterRevocation.Received.WaitAsync(Settle));

        // The sweep is the backstop, so the socket closes on its own within its bound.
        await closed.WaitAsync(RealtimeRevocationWorker.SweepInterval * 3);
    }

    [Fact]
    public async Task RemovedMembership_StopsNewPayloads()
    {
        using var client = Client(_factory);
        var owner = await SignInAsync(client);
        var member = await JoinAsShopStaffAsync(client, owner.ShopId);

        await using var hub = await ConnectAsync(_factory, member.AccessToken);

        using var first = Watch(hub);
        await CreateItemAsync(client, owner.AccessToken);
        await first.Received.WaitAsync(Settle);

        await RemoveMembershipAsync(owner.ShopId, member.UserId);

        using var afterRemoval = Watch(hub);
        await CreateItemAsync(client, owner.AccessToken);
        await Assert.ThrowsAsync<TimeoutException>(() => afterRemoval.Received.WaitAsync(Settle));
    }

    [Fact]
    public async Task RemovedMembership_CannotReconnectWithTheStillSignedToken()
    {
        using var client = Client(_factory);
        var owner = await SignInAsync(client);
        var member = await JoinAsShopStaffAsync(client, owner.ShopId);

        await RemoveMembershipAsync(owner.ShopId, member.UserId);

        // The token is untouched and still verifies; what is gone is the membership it names.
        var refusal = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var refused = await ConnectAsync(_factory, member.AccessToken);
        });

        // Asserted on the status so the test cannot pass for an unrelated reason — a mistyped path
        // or a fixture fault would otherwise satisfy it just as well.
        Assert.Contains(
            "401",
            refusal.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledAccount_StopsNewPayloads()
    {
        using var client = Client(_factory);
        var owner = await SignInAsync(client);
        var member = await JoinAsShopStaffAsync(client, owner.ShopId);

        await using var hub = await ConnectAsync(_factory, member.AccessToken);

        using var first = Watch(hub);
        await CreateItemAsync(client, owner.AccessToken);
        await first.Received.WaitAsync(Settle);

        await DisableAccountAsync(member.UserId);

        using var afterDisable = Watch(hub);
        await CreateItemAsync(client, owner.AccessToken);
        await Assert.ThrowsAsync<TimeoutException>(() => afterDisable.Received.WaitAsync(Settle));
    }

    [Fact]
    public async Task RevokeOnReplicaA_BlocksReplicaBDelivery()
    {
        using var clientA = Client(_factory);
        using var clientB = Client(_replicaB);
        var watcher = await SignInAsync(clientA);
        var actor = await SecondDeviceAsync(clientA, watcher);

        // The watcher's socket is held by replica B and the revocation is committed through replica
        // A. The mutation is issued through B because, with no SignalR backplane configured, a
        // payload only reaches sockets the dispatching process holds — so routing it through A
        // would assert nothing. What crosses replicas here is the authority decision: B refuses a
        // payload it is perfectly able to deliver, because A revoked the session.
        await using var hub = await ConnectAsync(_replicaB, watcher.AccessToken);

        using var first = Watch(hub);
        await CreateItemAsync(clientB, actor.AccessToken);
        await first.Received.WaitAsync(Settle);

        await RevokeSessionAsync(clientA, actor, watcher.SessionId);

        using var afterRevocation = Watch(hub);
        await CreateItemAsync(clientB, actor.AccessToken);
        await Assert.ThrowsAsync<TimeoutException>(() => afterRevocation.Received.WaitAsync(Settle));
    }

    [Fact]
    public async Task MissedRevocationWakeup_StillBlocksOutboundPayload()
    {
        // There is no wakeup to miss: authorization happens on the way out, so a replica that has
        // not swept yet still refuses. Run against a host with no sweep at all, because a clock
        // assertion cannot prove this — the timer's phase is unknown, so a sweep could tick inside
        // the window and produce the same silence with per-payload authorization removed entirely.
        using var client = Client(_withoutSweep);
        var watcher = await SignInAsync(client);
        var actor = await SecondDeviceAsync(client, watcher);

        await using var hub = await ConnectAsync(_withoutSweep, watcher.AccessToken);
        using var first = Watch(hub);
        await CreateItemAsync(client, actor.AccessToken);
        await first.Received.WaitAsync(Settle);

        await RevokeSessionAsync(client, actor, watcher.SessionId);

        using var afterRevocation = Watch(hub);
        await CreateItemAsync(client, actor.AccessToken);
        await Assert.ThrowsAsync<TimeoutException>(() => afterRevocation.Received.WaitAsync(Settle));
    }

    [Fact]
    public async Task DisconnectedTransport_LeavesNoLeaseBehind()
    {
        using var client = Client(_factory);
        var watcher = await SignInAsync(client);

        var hub = await ConnectAsync(_factory, watcher.AccessToken);
        await WaitForAsync(async () => await LeaseCountAsync(watcher.SessionId) == 1, Settle);

        await hub.DisposeAsync();

        await WaitForAsync(async () => await LeaseCountAsync(watcher.SessionId) == 0, Settle);
    }

    // ── Transport ───────────────────────────────────────────────────────────

    private static async Task<HubConnection> ConnectAsync(ApiWebApplicationFactory factory, string accessToken)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost/hubs/products"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

/// <summary>
    /// The next ProductAdded payload, armed before the mutation that should produce it. Disposable,
    /// because a negative case never completes the wait and an undisposed handler would still be
    /// attached when the next one is armed on the same hub.
    /// </summary>
    private sealed class Watcher : IDisposable
    {
        private readonly IDisposable _subscription;
        private readonly TaskCompletionSource<JsonElement> _next =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Watcher(HubConnection hub) =>
            _subscription = hub.On<JsonElement>("ProductAdded", payload => _next.TrySetResult(payload));

        public Task<JsonElement> Received => _next.Task;

        public void Dispose() => _subscription.Dispose();
    }

    private static Watcher Watch(HubConnection hub) => new(hub);

    private static Task ClosedSignal(HubConnection hub)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        return closed.Task;
    }

    // ── Identity and shop setup ─────────────────────────────────────────────

    private sealed record Device(Guid UserId, string Email, Guid ShopId, Guid SessionId, string AccessToken);

    private static HttpClient Client(ApiWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private async Task<Device> SignInAsync(HttpClient client)
    {
        var email = $"owner-{Guid.NewGuid():N}@test.com";
        var enrolled = await _factory.EnrollAsync(client, new
        {
            email,
            password = Password,
            firstName = "Shop",
            lastName = "Owner",
        });
        enrolled.EnsureSuccessStatusCode();
        var body = await enrolled.Content.ReadFromJsonAsync<JsonElement>();
        var userId = body.GetProperty("user").GetProperty("id").GetGuid();
        var token = body.GetProperty("accessToken").GetString()!;

        var created = await SendAsync(client, HttpMethod.Post, "/api/shops", token, new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        created.EnsureSuccessStatusCode();
        var shop = await created.Content.ReadFromJsonAsync<JsonElement>();
        var shopToken = shop.GetProperty("accessToken").GetString()!;

        return new Device(
            userId,
            email,
            shop.GetProperty("activeShopId").GetGuid(),
            ReadSessionId(shopToken),
            shopToken);
    }

    /// <summary>A second sign-in by the same account: another session, same shop.</summary>
    private static async Task<Device> SecondDeviceAsync(HttpClient client, Device first)
    {
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { identifier = first.Email, password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var switched = await SendAsync(client, HttpMethod.Post, "/api/shops/switch", token, new { shopId = first.ShopId });
        switched.EnsureSuccessStatusCode();
        var shopToken = (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        return first with { SessionId = ReadSessionId(shopToken), AccessToken = shopToken };
    }

    /// <summary>
    /// Fixture-only membership seeding. Public staff onboarding runs through invitations, which the
    /// membership suite exercises; what is needed here is the end state.
    /// </summary>
    private async Task<Device> JoinAsShopStaffAsync(HttpClient client, Guid shopId)
    {
        var member = await SignInAsync(client);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Add(ShopMembership.Create(shopId, member.UserId, ShopRole.Staff, isDefault: false));
            await db.SaveChangesAsync();
        }

        var switched = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/shops/switch",
            member.AccessToken,
            new { shopId });
        switched.EnsureSuccessStatusCode();
        var shopToken = (await switched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        return member with { ShopId = shopId, SessionId = ReadSessionId(shopToken), AccessToken = shopToken };
    }

    private async Task RemoveMembershipAsync(Guid shopId, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var membership = await db.ShopMemberships.SingleAsync(m => m.ShopId == shopId && m.UserId == userId);
        membership.ChangeAuthority(membership.Role, MembershipStatus.Removed);
        await db.SaveChangesAsync();
    }

    private async Task DisableAccountAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.SetLoginEnabled(false);
        await db.SaveChangesAsync();
    }

    private static async Task RevokeSessionAsync(HttpClient client, Device actor, Guid sessionId)
    {
        var revoked = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/users/me/sessions/{sessionId}",
            actor.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
    }

    private static async Task CreateItemAsync(HttpClient client, string accessToken)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await SendAsync(client, HttpMethod.Post, "/api/items", accessToken, new
        {
            name = $"Rice {suffix}",
            barcode = $"ITM-{suffix}",
            description = "Premium quality",
            uom = "kg",
            isActive = true,
            hsnCode = "10063090",
            defaultTaxRatePercent = 5m,
        });
        Assert.True(
            created.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            await created.Content.ReadAsStringAsync());
    }

    private async Task<int> LeaseCountAsync(Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.RealtimeConnectionLeases.CountAsync(lease => lease.SessionId == sessionId);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan within)
    {
        var deadline = DateTimeOffset.UtcNow + within;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }

        Assert.Fail($"The condition was still false after {within}.");
    }

    private static Guid ReadSessionId(string accessToken)
    {
        var payload = accessToken.Split('.')[1];
        var padded = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=')
            .Replace('-', '+')
            .Replace('_', '/');
        var claims = JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(padded));
        return claims.GetProperty("sid").GetGuid();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string accessToken,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
