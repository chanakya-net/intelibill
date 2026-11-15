using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0110Tests
{
    [Fact]
    public async Task RefreshKeepsTheSignInMfaTimeAndNeverOutlivesTwelveHours()
    {
        await using var signed = await SignInAsync();
        var end = signed.Clock.GetUtcNow() + TimeSpan.FromHours(12);
        signed.Clock.Advance(TimeSpan.FromMinutes(4));
        await AssertRefreshAsync(signed, HttpStatusCode.OK);
        await AssertStatusAsync(signed.Browser, Invitations, HttpStatusCode.OK);
        signed.Clock.Advance(TimeSpan.FromMinutes(1));
        await AssertStatusAsync(signed.Browser, Session, HttpStatusCode.OK);
        await AssertStatusAsync(signed.Browser, Invitations, HttpStatusCode.Forbidden);

        while (signed.Clock.GetUtcNow() + TimeSpan.FromMinutes(25) < end - TimeSpan.FromSeconds(1))
        {
            signed.Clock.Advance(TimeSpan.FromMinutes(25));
            await AssertRefreshAsync(signed, HttpStatusCode.OK);
        }

        signed.Clock.Advance(end - TimeSpan.FromSeconds(1) - signed.Clock.GetUtcNow());
        await AssertRefreshAsync(signed, HttpStatusCode.OK);
        await AssertStatusAsync(signed.Browser, Invitations, HttpStatusCode.Forbidden);
        Assert.Equal(1L, await CountAsync(signed, "admin_sessions WHERE mfa_verified_at = created_at"));
        signed.Clock.Advance(TimeSpan.FromSeconds(1));
        await AssertStatusAsync(signed.Browser, Session, HttpStatusCode.Unauthorized);
        await AssertRefreshAsync(signed, HttpStatusCode.Unauthorized);
        Assert.Equal(0L, await CountAsync(signed, """
            admin_refresh_credentials c JOIN admin_sessions s ON s.id = c.session_id
            WHERE c.expires_at > s.created_at + interval '12 hours'
            """));
    }

    [Fact]
    public async Task RefreshHonoursTheExactThirtyMinuteIdleBoundary()
    {
        await using var signed = await SignInAsync();
        signed.Clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1));
        await AssertRefreshAsync(signed, HttpStatusCode.OK);
        signed.Clock.Advance(TimeSpan.FromMinutes(30));
        await AssertRefreshAsync(signed, HttpStatusCode.Unauthorized);
        await AssertStatusAsync(signed.Browser, Session, HttpStatusCode.Unauthorized);
        Assert.Equal(2L, await CountAsync(signed, "admin_refresh_credentials"));
    }

    [Theory]
    [InlineData("UPDATE admin_sessions SET revoked_at = last_seen_at")]
    [InlineData("UPDATE admin_identities SET deactivated_at = mfa_enrolled_at")]
    public async Task EndedSessionsFailTheNextProtectedRequestAndCannotRefresh(string ending)
    {
        await using var signed = await SignInAsync();
        signed.Clock.Advance(TimeSpan.FromMinutes(1));
        await AssertRefreshAsync(signed, HttpStatusCode.OK);
        await LicensingPostgreSqlFixture.ExecuteAsync(signed.Runtime, ending);
        await AssertStatusAsync(signed.Browser, Session, HttpStatusCode.Unauthorized);
        await AssertRefreshAsync(signed, HttpStatusCode.Unauthorized);
        Assert.Equal(2L, await CountAsync(signed, "admin_refresh_credentials"));
    }

    private static async Task AssertRefreshAsync(SignedIn signed, HttpStatusCode status)
    {
        using var response = await RefreshAsync(signed.Browser, signed.Csrf);
        Assert.Equal(status, response.StatusCode);
    }
}
