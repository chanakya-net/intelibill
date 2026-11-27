using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Hash = "4F2C9A51E3B7D08C6A1F5E2B9D4C7A30E8B1F6D2C5A9E3B7F0D4C8A2E6B1F5D9";

    private static RefreshToken Issued(DateTimeOffset? expiresAt = null) =>
        RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), securityVersion: 3, Hash, expiresAt ?? Now.AddDays(7));

    [Fact]
    public void Issue_BindsTheSessionAndStoresOnlyTheHash()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var token = RefreshToken.Issue(userId, sessionId, securityVersion: 3, Hash, Now.AddDays(7));

        Assert.Equal(userId, token.UserId);
        Assert.Equal(sessionId, token.AuthSessionId);
        Assert.Equal(3, token.SecurityVersion);
        Assert.Equal(Hash, token.TokenHash);
        Assert.Null(token.ParentTokenId);
        Assert.Null(token.ConsumedAt);
        Assert.True(token.IsUsableAt(Now));
    }

    /// <summary>
    /// A credential renews exactly once. The successor stays in the same session, under the same
    /// security version, and names the credential it replaced so a second child is detectable.
    /// </summary>
    [Fact]
    public void Rotate_ConsumesThePredecessorAndLinksOneChild()
    {
        var token = Issued();
        const string successorHash = "0A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8F9";

        var successor = token.Rotate(successorHash, Now.AddDays(8), Now);

        Assert.Equal(Now, token.ConsumedAt);
        Assert.False(token.IsUsableAt(Now));
        Assert.Equal(token.Id, successor.ParentTokenId);
        Assert.Equal(token.AuthSessionId, successor.AuthSessionId);
        Assert.Equal(token.UserId, successor.UserId);
        Assert.Equal(token.SecurityVersion, successor.SecurityVersion);
        Assert.Equal(successorHash, successor.TokenHash);
        Assert.Equal(Now.AddDays(8), successor.ExpiresAt);
        Assert.True(successor.IsUsableAt(Now));
    }

    [Fact]
    public void Rotate_WhenAlreadyConsumed_Throws()
    {
        var token = Issued();
        token.Rotate(Hash, Now.AddDays(8), Now);

        Assert.Throws<InvalidOperationException>(() => token.Rotate(Hash, Now.AddDays(8), Now));
    }

    [Fact]
    public void Rotate_WhenRevoked_Throws()
    {
        var token = Issued();
        token.Revoke("signed-out", Now);

        Assert.Throws<InvalidOperationException>(() => token.Rotate(Hash, Now.AddDays(8), Now));
    }

    /// <summary>The expiry instant itself is already too late, so there is no tick in which both a
    /// renewal and an expiry check can pass.</summary>
    [Fact]
    public void IsUsableAt_ExactExpiry_IsFalse()
    {
        var token = Issued(expiresAt: Now);

        Assert.True(token.IsUsableAt(Now.AddTicks(-1)));
        Assert.False(token.IsUsableAt(Now));
    }

    [Fact]
    public void Revoke_RecordsTheReasonAndKeepsTheFirstOne()
    {
        var token = Issued();

        token.Revoke("refresh-reuse", Now);
        token.Revoke("signed-out", Now.AddMinutes(1));

        Assert.True(token.IsRevoked);
        Assert.Equal(Now, token.RevokedAt);
        Assert.Equal("refresh-reuse", token.RevocationReason);
        Assert.False(token.IsUsableAt(Now));
    }

    [Fact]
    public void Issue_RefusesAnEmptyHash()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), 1, " ", Now.AddDays(7)));
    }
}
