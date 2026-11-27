using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class PhoneChallengeTests
{
    private const int MaxAttempts = 5;
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static PhoneChallenge Create(DateTimeOffset? expiresAt = null) => PhoneChallenge.Create(
        "enrollment",
        "+919876543210",
        codeHash: "0123456789ABCDEF",
        keyId: "k1",
        expiresAt: expiresAt ?? Now.AddMinutes(5),
        resendAllowedAt: Now.AddSeconds(60));

    [Fact]
    public void Create_StartsOpenWithNoAttemptsSpent()
    {
        var challenge = Create();

        Assert.True(challenge.IsOpen(Now, MaxAttempts));
        Assert.Equal(0, challenge.Attempts);
        Assert.Null(challenge.ConsumedAt);
    }

    [Fact]
    public void RegisterFailedAttempt_FifthWrongAttempt_ClosesChallenge()
    {
        var challenge = Create();

        for (var attempt = 1; attempt < MaxAttempts; attempt++)
        {
            Assert.False(challenge.RegisterFailedAttempt(MaxAttempts));
            Assert.True(challenge.IsOpen(Now, MaxAttempts));
        }

        Assert.True(challenge.RegisterFailedAttempt(MaxAttempts));
        Assert.False(challenge.IsOpen(Now, MaxAttempts));
        Assert.Equal(MaxAttempts, challenge.Attempts);
    }

    /// <summary>
    /// The expiry instant itself is too late. A boundary that included it would make the lifetime
    /// one tick longer than the policy the code was issued under.
    /// </summary>
    [Fact]
    public void IsOpen_AtAndAfterExpiry_IsFalse()
    {
        var expiresAt = Now.AddMinutes(5);
        var challenge = Create(expiresAt);

        Assert.True(challenge.IsOpen(expiresAt.AddTicks(-1), MaxAttempts));
        Assert.False(challenge.IsOpen(expiresAt, MaxAttempts));
        Assert.False(challenge.IsOpen(expiresAt.AddTicks(1), MaxAttempts));
    }

    [Fact]
    public void Consume_IsSingleUse()
    {
        var challenge = Create();

        challenge.Consume(Now);

        Assert.Equal(Now, challenge.ConsumedAt);
        Assert.False(challenge.IsOpen(Now, MaxAttempts));
    }

    [Fact]
    public void Create_DoesNotKeepTheCodeItself()
    {
        var challenge = Create();

        Assert.Equal("0123456789ABCDEF", challenge.CodeHash);
        Assert.Equal("k1", challenge.KeyId);
    }
}
