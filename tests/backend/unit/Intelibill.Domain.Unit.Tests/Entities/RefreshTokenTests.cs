using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class RefreshTokenTests
{
    [Fact]
    public void Create_SetsExpectedProperties()
    {
        var userId = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);

        var token = RefreshToken.Create(userId, "token-value", expires);

        Assert.Equal(userId, token.UserId);
        Assert.Equal("token-value", token.Token);
        Assert.Equal(expires, token.ExpiresAt);
        Assert.False(token.IsRevoked);
        Assert.Null(token.RevokedAt);
    }

    [Fact]
    public void IsExpired_WhenPastExpiry_ReturnsTrue()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.True(token.IsExpired);
        Assert.False(token.IsActive);
    }

    [Fact]
    public void Revoke_SetsRevokedFlags()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddHours(1));

        token.Revoke();

        Assert.True(token.IsRevoked);
        Assert.NotNull(token.RevokedAt);
        Assert.False(token.IsActive);
    }

    /// <summary>
    /// A refresh credential belongs to the device whose session earned it, so revoking that one
    /// device also ends its refresh family without touching the account's other devices.
    /// </summary>
    [Fact]
    public void BindSession_TiesTheTokenToOneDeviceSession()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddHours(1));
        var sessionId = Guid.NewGuid();

        token.BindSession(sessionId);

        Assert.Equal(sessionId, token.AuthSessionId);
    }

    [Fact]
    public void Create_LeavesTheSessionUnboundUntilItIsKnown()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddHours(1));

        Assert.Null(token.AuthSessionId);
    }
}
