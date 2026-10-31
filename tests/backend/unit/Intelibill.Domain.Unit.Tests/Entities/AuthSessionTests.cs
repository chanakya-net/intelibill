using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class AuthSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static AuthSession Start(Guid? shopId = null, DeviceMetadata? device = null) =>
        AuthSession.Start(
            userId: Guid.NewGuid(),
            securityVersion: 7,
            recoveryEpoch: "1",
            activeShopId: shopId,
            device: device ?? DeviceMetadata.Unknown,
            now: Now,
            expiresAt: Now.AddDays(7));

    [Fact]
    public void Start_RecordsTheProofItWasAcceptedUnderAndIsActive()
    {
        var shopId = Guid.NewGuid();

        var session = Start(shopId, new DeviceMetadata("Chrome on Windows", "203.0.113.4"));

        Assert.Equal(7, session.SecurityVersion);
        Assert.Equal("1", session.RecoveryEpoch);
        Assert.Equal(shopId, session.ActiveShopId);
        Assert.Equal(0, session.ScopeVersion);
        Assert.Equal("Chrome on Windows", session.UserAgent);
        Assert.Equal("203.0.113.4", session.CreatedFromIp);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Null(session.RevokedAt);
        Assert.Null(session.RevocationReason);
        Assert.True(session.IsActive(Now));
    }

    [Fact]
    public void Revoke_RecordsReasonAndTimeAndEndsTheSession()
    {
        var session = Start();

        session.Revoke("password-changed", Now.AddHours(1));

        Assert.Equal(Now.AddHours(1), session.RevokedAt);
        Assert.Equal("password-changed", session.RevocationReason);
        Assert.False(session.IsActive(Now.AddHours(1)));
    }

    [Fact]
    public void Revoke_IsIdempotentAndKeepsTheFirstReason()
    {
        var session = Start();
        session.Revoke("user-revoked", Now.AddHours(1));

        session.Revoke("password-changed", Now.AddHours(2));

        Assert.Equal(Now.AddHours(1), session.RevokedAt);
        Assert.Equal("user-revoked", session.RevocationReason);
    }

    [Fact]
    public void IsActive_AtOrAfterExpiry_IsFalse()
    {
        var session = Start();

        Assert.False(session.IsActive(Now.AddDays(7)));
        Assert.True(session.IsActive(Now.AddDays(7).AddSeconds(-1)));
    }

    [Fact]
    public void ChangeScope_ToAnotherShop_BumpsScopeVersion()
    {
        var session = Start(Guid.NewGuid());
        var target = Guid.NewGuid();

        session.ChangeScope(target, Now.AddMinutes(5));

        Assert.Equal(target, session.ActiveShopId);
        Assert.Equal(1, session.ScopeVersion);
        Assert.Equal(Now.AddMinutes(5), session.LastSeenAt);
    }

    /// <summary>
    /// Re-selecting the shop a device is already in must not bump the scope version: that would
    /// invalidate the access token the client is holding for no change at all.
    /// </summary>
    [Fact]
    public void ChangeScope_ToTheSameShop_LeavesScopeVersionAlone()
    {
        var shopId = Guid.NewGuid();
        var session = Start(shopId);

        session.ChangeScope(shopId, Now.AddMinutes(5));

        Assert.Equal(0, session.ScopeVersion);
    }

    [Fact]
    public void Touch_MovesLastSeenForwardOnly()
    {
        var session = Start();

        session.Touch(Now.AddMinutes(10));
        session.Touch(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(10), session.LastSeenAt);
    }

    /// <summary>
    /// A device label is a description the user types so they can recognise the device. It is never
    /// read as authentication, so it carries no authority and is simply stored.
    /// </summary>
    [Fact]
    public void Relabel_StoresATrimmedLabelAndTreatsBlankAsNone()
    {
        var session = Start();

        session.Relabel("  Shop counter tablet  ");
        Assert.Equal("Shop counter tablet", session.DeviceLabel);

        session.Relabel("   ");
        Assert.Null(session.DeviceLabel);
    }
}
