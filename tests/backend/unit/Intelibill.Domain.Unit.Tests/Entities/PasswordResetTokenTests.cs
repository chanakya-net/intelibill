using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class PasswordResetTokenTests
{
    [Fact]
    public void Create_SetsExpectedProperties()
    {
        var before = DateTimeOffset.UtcNow;

        var token = PasswordResetToken.Create(Guid.NewGuid(), "hash-value", expiryHours: 3);

        Assert.Equal("hash-value", token.TokenHash);
        Assert.False(token.IsUsed);
        Assert.True(token.ExpiresAt >= before.AddHours(3).AddSeconds(-2));
        Assert.True(token.IsValid);
    }

    [Fact]
    public void IsExpired_WhenPastExpiry_ReturnsTrue()
    {
        var token = PasswordResetToken.Create(Guid.NewGuid(), "hash", expiryHours: -1);

        Assert.True(token.IsExpired);
        Assert.False(token.IsValid);
    }

    [Fact]
    public void MarkAsUsed_UpdatesValidity()
    {
        var token = PasswordResetToken.Create(Guid.NewGuid(), "hash");

        token.MarkAsUsed();

        Assert.True(token.IsUsed);
        Assert.False(token.IsValid);
    }
}