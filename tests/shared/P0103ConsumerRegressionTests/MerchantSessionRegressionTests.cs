using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.P0103ConsumerRegressionTests;

public sealed partial class ConsumerTests
{
    [Fact]
    public async Task PreExtractionRefreshCredentialStillRotates()
    {
        var merchant = await database.SeedMerchantAsync();
        using var host = new MerchantHost(database.MerchantRuntime);
        using var client = SecurityFixtures.HttpsClient(host);
        var auth = await LoginAsync(client, merchant.Email!);
        var parent = await StoreLegacyRefreshAsync(merchant, auth.SessionId!.Value, auth.RefreshTokenExpiresAt, SecurityFixtures.LegacyHash);

        using var result = await RefreshAsync(client, SecurityFixtures.LegacyToken);
        var successor = await ReadRenewalAsync(result);
        await AssertRefreshChainAsync(parent, successor, revoked: false);
        Assert.Equal(HttpStatusCode.OK, await ProtectedStatusAsync(client, successor.AccessToken));

        using var replay = await RefreshAsync(client, SecurityFixtures.LegacyToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        await AssertRefreshChainAsync(parent, successor, revoked: true);
        Assert.Equal(HttpStatusCode.Unauthorized, await ProtectedStatusAsync(client, successor.AccessToken));
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.OK)]
    [InlineData(0, HttpStatusCode.Unauthorized)]
    public async Task RefreshExpiryUsesTheExactClockBoundary(int seconds, HttpStatusCode expected)
    {
        var merchant = await database.SeedMerchantAsync();
        using var host = new MerchantHost(database.MerchantRuntime);
        using var client = SecurityFixtures.HttpsClient(host);
        var auth = await LoginAsync(client, merchant.Email!);
        // Shorter than the session lifetime so the result isolates credential expiry.
        var expires = DateTimeOffset.FromUnixTimeSeconds(auth.RefreshTokenExpiresAt.AddDays(-1).ToUnixTimeSeconds());
        host.Clock.Set(expires.AddSeconds(seconds));
        var (legacyToken, hash) = seconds < 0
            ? SecurityFixtures.LegacyBeforeExpiry
            : SecurityFixtures.LegacyAtExpiry;
        var parent = await StoreLegacyRefreshAsync(merchant, auth.SessionId!.Value, expires, hash);

        using var result = await RefreshAsync(client, legacyToken);
        Assert.Equal(expected, result.StatusCode);
        await using var context = database.MerchantOwnerContext();
        Assert.Equal(seconds < 0, await context.RefreshTokens.AnyAsync(token => token.ParentTokenId == parent.Id));
    }

    [Fact]
    public async Task ConcurrentRefreshesIssueOneSuccessorAndRevokeTheFamily()
    {
        var merchant = await database.SeedMerchantAsync();
        using var host = new MerchantHost(database.MerchantRuntime);
        using var client = SecurityFixtures.HttpsClient(host);
        var auth = await LoginAsync(client, merchant.Email!);
        var (legacyToken, hash) = SecurityFixtures.LegacyConcurrent;
        var parent = await StoreLegacyRefreshAsync(merchant, auth.SessionId!.Value, auth.RefreshTokenExpiresAt, hash);

        await using var connection = new NpgsqlConnection(database.Bootstrap("merchant").ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockMerchantAsync(transaction, merchant.Id);
        var first = RefreshAsync(client, legacyToken);
        var second = RefreshAsync(client, legacyToken);
        try
        {
            await WaitForRefreshesOnUserLockAsync();
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        using var firstResult = await first;
        using var secondResult = await second;
        var results = new[] { firstResult, secondResult };
        Assert.Single(results, result => result.StatusCode == HttpStatusCode.Unauthorized);
        var successor = await ReadRenewalAsync(Assert.Single(results, result => result.StatusCode == HttpStatusCode.OK));
        await AssertRefreshChainAsync(parent, successor, revoked: true);
        Assert.Equal(HttpStatusCode.Unauthorized, await ProtectedStatusAsync(client, successor.AccessToken));
        using var renewRevoked = await RefreshAsync(client, successor.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, renewRevoked.StatusCode);
    }

    private static async Task LockMerchantAsync(NpgsqlTransaction transaction, Guid userId)
    {
        await using var command = new NpgsqlCommand("SELECT id FROM users WHERE id = $1 FOR UPDATE",
            transaction.Connection, transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = userId });
        await command.ExecuteScalarAsync();
    }

    private async Task WaitForRefreshesOnUserLockAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var connection = new NpgsqlConnection(database.Bootstrap("merchant").ConnectionString);
        await connection.OpenAsync(deadline.Token);
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND usename = 'merchant_runtime'
              AND wait_event_type = 'Lock' AND query LIKE '%FROM users%FOR UPDATE%'
            """, connection);
        while ((long?)await command.ExecuteScalarAsync(deadline.Token) != 2)
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
    }

    private async Task<RefreshToken> StoreLegacyRefreshAsync(User user, Guid sessionId, DateTimeOffset expiry, string hash)
    {
        await using var context = database.MerchantOwnerContext();
        var parent = RefreshToken.Issue(user.Id, sessionId, user.SecurityVersion, hash, expiry);
        context.RefreshTokens.Add(parent);
        await context.SaveChangesAsync();
        return parent;
    }

    private async Task AssertRefreshChainAsync(RefreshToken parent, AuthResult successor, bool revoked)
    {
        await using var context = database.MerchantOwnerContext();
        var savedParent = await context.RefreshTokens.SingleAsync(token => token.Id == parent.Id);
        var savedChild = await context.RefreshTokens.SingleAsync(token => token.ParentTokenId == parent.Id);
        var session = await context.AuthSessions.SingleAsync(value => value.Id == parent.AuthSessionId);
        Assert.NotNull(savedParent.ConsumedAt);
        Assert.Equal(parent.AuthSessionId, savedChild.AuthSessionId);
        Assert.Equal(savedChild.AuthSessionId, successor.SessionId);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(successor.RefreshToken)));
        Assert.True(string.Equals(expectedHash, savedChild.TokenHash, StringComparison.Ordinal), "Only a SHA-256 hash is stored.");
        Assert.Equal(revoked, savedChild.IsRevoked);
        Assert.Equal(revoked, session.RevokedAt.HasValue);
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/token/refresh", new { refreshToken });

    private static async Task<AuthResult> ReadRenewalAsync(HttpResponseMessage result)
    {
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var response = await result.Content.ReadFromJsonAsync<JsonElement>();
        string[] expected =
        [
            "accessToken", "accessTokenExpiresAt", "activeShopId", "refreshToken",
            "refreshTokenExpiresAt", "sessionId", "shops", "user",
        ];
        Assert.Equal(expected, response.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        return response.Deserialize<AuthResult>(JsonSerializerOptions.Web)!;
    }
}
