using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

/// <summary>
/// A refresh credential is single-use. These tests run real requests against real PostgreSQL, each
/// with its own DbContext, and hold one of them at a chosen point so the interleaving under test is
/// the one that actually happens rather than whichever the scheduler happened to pick.
/// </summary>
[Collection("Integration Tests")]
public sealed class RefreshRotationConcurrencyTests(PostgreSqlTestFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string Password = "OwnersPassword!1";

    private readonly ApiWebApplicationFactory _factory = new(fixture, services =>
    {
        var repository = services.Single(x => x.ServiceType == typeof(IAuthSessionRepository));
        services.Remove(repository);
        services.AddSingleton<RotationBarrier>();
        services.AddScoped<IAuthSessionRepository>(sp => new BarrierAuthSessionRepository(
            (IAuthSessionRepository)ActivatorUtilities.CreateInstance(sp, repository.ImplementationType!),
            sp.GetRequiredService<RotationBarrier>()));
    });

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    private RotationBarrier Barrier => _factory.Services.GetRequiredService<RotationBarrier>();

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string Email() => $"owner-{Guid.NewGuid():N}@test.com";

    private async Task<SignedIn> EnrollAsync(HttpClient client, string email)
    {
        var response = await _factory.EnrollAsync(client, new
        {
            email,
            password = Password,
            firstName = "Rotation",
            lastName = "Owner",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SignedIn.From(await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken });

    private static async Task<HttpStatusCode> GetShopsAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shops/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (await client.SendAsync(request)).StatusCode;
    }

    private async Task<int> LiveSessionCountAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuthSessions.CountAsync(x => x.UserId == userId && x.RevokedAt == null);
    }

    // ── Two renewals of one credential ──────────────────────────────────────────

    /// <summary>
    /// Both requests have read the credential before either has locked anything — the interleaving
    /// a slow network and two browser tabs produce. One may succeed. The other has presented a
    /// credential that was already used, which is indistinguishable from a replay of a stolen one,
    /// so it ends the session: nothing either request was handed may keep working.
    /// </summary>
    [Fact]
    public async Task ParallelRefresh_AtMostOneSuccessAndNoLiveBranchAfterReuse()
    {
        using var tabA = Client();
        using var tabB = Client();
        var signedIn = await EnrollAsync(tabA, Email());

        var pause = Barrier.HoldBeforeLock(parties: 2);
        var first = RefreshAsync(tabA, signedIn.RefreshToken);
        var second = RefreshAsync(tabB, signedIn.RefreshToken);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        pause.Release();

        var responses = await Task.WhenAll(first, second);

        var winner = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);

        var issued = SignedIn.From(await winner.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tabA, issued.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetShopsAsync(tabA, issued.AccessToken));
        Assert.Equal(0, await LiveSessionCountAsync(signedIn.UserId));
    }

    private sealed record SignedIn(Guid UserId, Guid SessionId, string AccessToken, string RefreshToken)
    {
        public static SignedIn From(JsonElement body) => new(
            body.GetProperty("user").GetProperty("id").GetGuid(),
            body.GetProperty("sessionId").GetGuid(),
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("refreshToken").GetString()!);
    }

    /// <summary>
    /// Holds requests at one of two points: on the way into the account lock, after the credential
    /// has been looked up but before anything is locked; or on the way into commit, holding every
    /// lock the transition took. One-shot: once the named number of requests have arrived it
    /// disarms, so the competing request passes straight through.
    /// </summary>
    internal sealed class RotationBarrier
    {
        private readonly Lock _gate = new();
        private Pause? _beforeLock;
        private Pause? _beforeCommit;

        public Pause HoldBeforeLock(int parties = 1)
        {
            lock (_gate)
                return _beforeLock = new Pause(parties);
        }

        public Pause HoldBeforeCommit()
        {
            lock (_gate)
                return _beforeCommit = new Pause(1);
        }

        public Task AtLockAsync() => Arrive(ref _beforeLock);

        public Task AtCommitAsync() => Arrive(ref _beforeCommit);

        private Task Arrive(ref Pause? slot)
        {
            Pause? pause;
            lock (_gate)
            {
                pause = slot;
                if (pause is null)
                    return Task.CompletedTask;
                if (pause.ArriveAndCheckFull())
                    slot = null;
            }

            return pause.WaitAsync();
        }
    }

    internal sealed class Pause(int parties)
    {
        private int _remaining = parties;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => Released.TrySetResult();

        internal bool ArriveAndCheckFull()
        {
            _remaining--;
            if (_remaining > 0)
                return false;
            Reached.TrySetResult();
            return true;
        }

        internal Task WaitAsync() => Released.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class BarrierAuthSessionRepository(IAuthSessionRepository inner, RotationBarrier barrier) : IAuthSessionRepository
    {
        public Task<IAsyncDisposable> BeginAsync(CancellationToken ct) => inner.BeginAsync(ct);

        public async Task<UserAuthority?> LockUserAuthorityAsync(Guid userId, CancellationToken ct)
        {
            await barrier.AtLockAsync();
            return await inner.LockUserAuthorityAsync(userId, ct);
        }

        public Task AddAsync(AuthSession session, CancellationToken ct) => inner.AddAsync(session, ct);

        public Task<AuthSession?> FindAsync(Guid userId, Guid sessionId, CancellationToken ct) => inner.FindAsync(userId, sessionId, ct);

        public Task<AuthSession?> LockAsync(Guid userId, Guid sessionId, CancellationToken ct) => inner.LockAsync(userId, sessionId, ct);

        public Task<IReadOnlyList<AuthSession>> ListAsync(Guid userId, CancellationToken ct) => inner.ListAsync(userId, ct);

        public Task<int> RevokeAllAsync(Guid userId, Guid? exceptSessionId, string reason, DateTimeOffset now, CancellationToken ct) =>
            inner.RevokeAllAsync(userId, exceptSessionId, reason, now, ct);

        public Task RevokeAsync(AuthSession session, string reason, DateTimeOffset now, CancellationToken ct) =>
            inner.RevokeAsync(session, reason, now, ct);

        public async Task<bool> CommitAsync(CancellationToken ct)
        {
            await barrier.AtCommitAsync();
            return await inner.CommitAsync(ct);
        }
    }
}
