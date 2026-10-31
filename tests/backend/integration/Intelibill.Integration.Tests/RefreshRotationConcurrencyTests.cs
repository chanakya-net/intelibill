using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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

        // Reads the system clock until a test freezes it.
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<FreezableClock>();
        services.AddSingleton<TimeProvider>(sp => sp.GetRequiredService<FreezableClock>());

        // Serilog owns the pipeline in the app; here every ILogger the app resolves writes to memory
        // so a test can read back exactly what would have been logged.
        services.AddSingleton<LogCapture>();
        services.RemoveAll<ILoggerFactory>();
        services.AddSingleton<ILoggerFactory>(sp => LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(sp.GetRequiredService<LogCapture>())));

        services.RemoveAll<IEmailService>();
        services.AddSingleton<SessionLifecycleIntegrationTests.ResetLinkCapture>();
        services.AddSingleton<IEmailService>(sp => sp.GetRequiredService<SessionLifecycleIntegrationTests.ResetLinkCapture>());
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

    private FreezableClock Clock => _factory.Services.GetRequiredService<FreezableClock>();

    private LogCapture Logs => _factory.Services.GetRequiredService<LogCapture>();

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

    private static async Task<SignedIn> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SignedIn.From(await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken });

    private static async Task<SignedIn> RenewAsync(HttpClient client, string refreshToken)
    {
        var response = await RefreshAsync(client, refreshToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SignedIn.From(await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static Task<HttpResponseMessage> SignOutAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/token/revoke", new { refreshToken });

    private async Task<string> RequestResetTokenAsync(HttpClient client, string email)
    {
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/password-reset/request", new { email })).StatusCode);
        var link = _factory.Services.GetRequiredService<SessionLifecycleIntegrationTests.ResetLinkCapture>().ResetLink;
        Assert.NotNull(link);
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link).Query)["token"].ToString();
    }

    private static Task<HttpResponseMessage> ConfirmResetAsync(HttpClient client, string email, string token) =>
        client.PostAsJsonAsync("/api/auth/password-reset/confirm", new { email, token, newPassword = "RecoveredPassword!1" });

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

    /// <summary>
    /// Credentials that could be exchanged right now: never used, never revoked, not expired. Every
    /// descendant of every chain on the account is counted, not just the latest one.
    /// </summary>
    private async Task<int> UsableCredentialCountAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        return await db.RefreshTokens.CountAsync(x =>
            x.UserId == userId && !x.IsRevoked && x.ConsumedAt == null && x.ExpiresAt > now);
    }

    private async Task<int> SuccessorCountAsync(string presentedToken)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hash = RefreshTokenSecrets.Hash(presentedToken);
        var id = await db.RefreshTokens.Where(x => x.TokenHash == hash).Select(x => x.Id).SingleAsync();
        return await db.RefreshTokens.CountAsync(x => x.ParentTokenId == id);
    }

    /// <summary>
    /// Waits until some backend is blocked on a lock — proof that the competing request has reached
    /// the account lock and queued behind the one being held, rather than merely having been sent.
    /// </summary>
    private async Task WaitForLockWaiterAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var waiting = await db.Database
                .SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
                .SingleAsync();
            if (waiting > 0)
                return;
            await Task.Delay(20);
        }

        throw new TimeoutException("The competing request never queued behind the held lock.");
    }

    private async Task AssertNothingSurvivesAsync(Guid userId, HttpClient client, params SignedIn[] issued)
    {
        Assert.Equal(0, await UsableCredentialCountAsync(userId));
        Assert.Equal(0, await LiveSessionCountAsync(userId));
        foreach (var credentials in issued)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await GetShopsAsync(client, credentials.AccessToken));
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, credentials.RefreshToken)).StatusCode);
        }
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
        Assert.True(await SuccessorCountAsync(signedIn.RefreshToken) <= 1, "A credential may have at most one successor.");
        Assert.Equal(0, await UsableCredentialCountAsync(signedIn.UserId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tabA, issued.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetShopsAsync(tabA, issued.AccessToken));
        Assert.Equal(0, await LiveSessionCountAsync(signedIn.UserId));
    }

    // ── What is stored ──────────────────────────────────────────────────────────

    /// <summary>
    /// Neither the credential a client presented nor the one it was handed back appears anywhere in
    /// the table or in anything the app logged. What is stored is the hash, and it finds the row.
    /// </summary>
    [Fact]
    public async Task RefreshDatabase_ContainsHashOnly()
    {
        using var client = Client();
        var signedIn = await EnrollAsync(client, Email());
        var renewed = await RenewAsync(client, signedIn.RefreshToken);
        string[] secrets = [signedIn.RefreshToken, renewed.RefreshToken];

        List<string> rows;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            rows = await db.Database
                .SqlQuery<string>($"SELECT row_to_json(rt)::text AS \"Value\" FROM refresh_tokens rt WHERE rt.user_id = {signedIn.UserId}")
                .ToListAsync();
            var hashes = await db.RefreshTokens.Where(x => x.UserId == signedIn.UserId).Select(x => x.TokenHash).ToListAsync();
            Assert.Equal(secrets.Select(RefreshTokenSecrets.Hash).Order(), hashes.Order());
        }

        Assert.Equal(2, rows.Count);
        foreach (var secret in secrets)
        {
            Assert.All(rows, row => Assert.DoesNotContain(secret, row, StringComparison.Ordinal));
            Assert.All(Logs.Entries, entry => Assert.DoesNotContain(secret, entry, StringComparison.Ordinal));
        }
    }

    // ── Replays ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A credential that was already exchanged comes back — a stolen copy, or a client that lost the
    /// response. Its session ends, successor included, and the replay is recorded by opaque ids. The
    /// account's other device was not part of that chain and keeps working.
    /// </summary>
    [Fact]
    public async Task ConsumedPredecessor_RevokesItsFamilyOnly()
    {
        using var deviceA = Client();
        using var deviceB = Client();
        var email = Email();
        var a = await EnrollAsync(deviceA, email);
        var b = await LoginAsync(deviceB, email);
        var renewedA = await RenewAsync(deviceA, a.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(deviceA, a.RefreshToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetShopsAsync(deviceA, renewedA.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(deviceA, renewedA.RefreshToken)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, await GetShopsAsync(deviceB, b.AccessToken));
        Assert.Equal(1, await LiveSessionCountAsync(a.UserId));
        await RenewAsync(deviceB, b.RefreshToken);

        Assert.Contains(Logs.Entries, entry =>
            entry.StartsWith("Warning|", StringComparison.Ordinal) &&
            entry.Contains(a.SessionId.ToString(), StringComparison.Ordinal) &&
            !entry.Contains(a.RefreshToken, StringComparison.Ordinal));
    }

    /// <summary>
    /// A value that was never issued proves nothing about anyone's session, so presenting it ends
    /// nothing — otherwise guessing would be a way to sign other people out.
    /// </summary>
    [Fact]
    public async Task UnknownToken_DoesNotRevokeSessions()
    {
        using var client = Client();
        var signedIn = await EnrollAsync(client, Email());
        var unknown = RefreshTokenSecrets.NewToken();

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, unknown)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignOutAsync(client, unknown)).StatusCode);

        Assert.Equal(1, await LiveSessionCountAsync(signedIn.UserId));
        Assert.Equal(HttpStatusCode.OK, await GetShopsAsync(client, signedIn.AccessToken));
        await RenewAsync(client, signedIn.RefreshToken);
    }

    // ── Time ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The expiry instant is already too late. Checked against a frozen clock at exactly that
    /// instant, and one second before it, rather than by sleeping and hoping.
    /// </summary>
    [Fact]
    public async Task RefreshAtExpiry_IsRejected()
    {
        using var client = Client();
        var signedIn = await EnrollAsync(client, Email());

        Clock.Freeze(signedIn.RefreshTokenExpiresAt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, signedIn.RefreshToken)).StatusCode);

        // Refused for expiry, not as a replay: nothing was consumed or revoked, so a moment earlier
        // the same credential still renews.
        Clock.Freeze(signedIn.RefreshTokenExpiresAt.AddSeconds(-1));
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, signedIn.RefreshToken)).StatusCode);
    }

    // ── Security actions overlapping a renewal ──────────────────────────────────

    public enum Winner
    {
        Refresh,
        SecurityAction,
    }

    /// <summary>
    /// Sign-out and a renewal of the same credential, in both orders. If the renewal commits first,
    /// sign-out arrives holding a credential that was just used and ends the session as a replay;
    /// if sign-out commits first, the renewal finds its credential revoked. Either way, once sign-out
    /// has answered nothing in the chain works.
    /// </summary>
    [Theory]
    [InlineData(Winner.Refresh)]
    [InlineData(Winner.SecurityAction)]
    public async Task LogoutRacingRefresh_NoSurvivingSuccessor(Winner winner)
    {
        using var client = Client();
        var signedIn = await EnrollAsync(client, Email());

        if (winner == Winner.Refresh)
        {
            var held = Barrier.HoldBeforeCommit();
            var refresh = RefreshAsync(client, signedIn.RefreshToken);
            await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));

            var signOut = SignOutAsync(client, signedIn.RefreshToken);
            await WaitForLockWaiterAsync();
            held.Release();

            var renewed = await refresh;
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await signOut).StatusCode);
            await AssertNothingSurvivesAsync(
                signedIn.UserId,
                client,
                signedIn,
                SignedIn.From(await renewed.Content.ReadFromJsonAsync<JsonElement>()));
        }
        else
        {
            var held = Barrier.HoldBeforeLock();
            var refresh = RefreshAsync(client, signedIn.RefreshToken);
            await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(HttpStatusCode.NoContent, (await SignOutAsync(client, signedIn.RefreshToken)).StatusCode);
            held.Release();

            Assert.Equal(HttpStatusCode.Unauthorized, (await refresh).StatusCode);
            Assert.Equal(0, await SuccessorCountAsync(signedIn.RefreshToken));
            await AssertNothingSurvivesAsync(signedIn.UserId, client, signedIn);
        }
    }

    /// <summary>
    /// Password recovery and a renewal, in both orders. Recovery ends every device; a renewal that
    /// committed just before it must not leave its successor behind, and one that arrives after it
    /// must find the account moved on.
    /// </summary>
    [Theory]
    [InlineData(Winner.Refresh)]
    [InlineData(Winner.SecurityAction)]
    public async Task ResetRacingRefresh_NoSurvivingSuccessor(Winner winner)
    {
        using var client = Client();
        var email = Email();
        var signedIn = await EnrollAsync(client, email);
        var resetToken = await RequestResetTokenAsync(client, email);

        if (winner == Winner.Refresh)
        {
            var held = Barrier.HoldBeforeCommit();
            var refresh = RefreshAsync(client, signedIn.RefreshToken);
            await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));

            var reset = ConfirmResetAsync(client, email, resetToken);
            await WaitForLockWaiterAsync();
            held.Release();

            var renewed = await refresh;
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await reset).StatusCode);
            await AssertNothingSurvivesAsync(
                signedIn.UserId,
                client,
                signedIn,
                SignedIn.From(await renewed.Content.ReadFromJsonAsync<JsonElement>()));
        }
        else
        {
            var held = Barrier.HoldBeforeLock();
            var refresh = RefreshAsync(client, signedIn.RefreshToken);
            await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(HttpStatusCode.OK, (await ConfirmResetAsync(client, email, resetToken)).StatusCode);
            held.Release();

            Assert.Equal(HttpStatusCode.Unauthorized, (await refresh).StatusCode);
            Assert.Equal(0, await SuccessorCountAsync(signedIn.RefreshToken));
            await AssertNothingSurvivesAsync(signedIn.UserId, client, signedIn);
        }
    }

    private sealed record SignedIn(
        Guid UserId,
        Guid SessionId,
        string AccessToken,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt)
    {
        public static SignedIn From(JsonElement body) => new(
            body.GetProperty("user").GetProperty("id").GetGuid(),
            body.GetProperty("sessionId").GetGuid(),
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("refreshToken").GetString()!,
            body.GetProperty("refreshTokenExpiresAt").GetDateTimeOffset());
    }

    /// <summary>The system clock until frozen; then exactly the frozen instant, every time.</summary>
    internal sealed class FreezableClock : TimeProvider
    {
        private DateTimeOffset? _frozen;

        public void Freeze(DateTimeOffset at) => _frozen = at;

        public override DateTimeOffset GetUtcNow() => _frozen ?? System.GetUtcNow();
    }

    /// <summary>Every log entry the app writes, rendered with its structured values.</summary>
    internal sealed class LogCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyCollection<string> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(";", pairs.Select(x => $"{x.Key}={x.Value}"))
                    : string.Empty;
                entries.Enqueue($"{logLevel}|{category}|{formatter(state, exception)}|{values}|{exception}");
            }
        }
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
