using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0110Tests
{
    [Fact]
    public async Task RefreshRequiresExactOriginAndCsrfBeforeTouchingTheCredential()
    {
        await using var signed = await SignInAsync();
        using var wrongOrigin = await RefreshAsync(signed.Browser, signed.Csrf, "https://other.example.test");
        using var missingProof = await RefreshAsync(signed.Browser, null);
        using var malformedProof = await RefreshAsync(signed.Browser, "not-a-valid-proof");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missingProof.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, malformedProof.StatusCode);
        Assert.Equal(0L, await CountAsync(signed, "admin_refresh_credentials WHERE consumed_at IS NOT NULL"));

        await AssertRefreshAsync(signed, HttpStatusCode.OK);
    }

    [Fact]
    public async Task MissingOrUnknownCredentialsAreRefusedWithoutEndingTheSession()
    {
        await using var signed = await SignInAsync();
        using var anonymous = signed.Browser.Fork(RawClient(signed.Api));
        anonymous.Forget(RefreshCookie);
        using var missing = await RefreshAsync(anonymous, signed.Csrf);
        anonymous.Set(RefreshCookie, "r1_" + new string('A', 43));
        using var unknown = await RefreshAsync(anonymous, signed.Csrf);
        anonymous.Set(RefreshCookie, new string('x', 5000));
        using var oversized = await RefreshAsync(anonymous, signed.Csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oversized.StatusCode);
        Assert.Equal(0L, await CountAsync(signed, "admin_sessions WHERE revoked_at IS NOT NULL"));
        Assert.Equal(0L, await CountAsync(signed, $"provider_audit_events WHERE action = {ReuseRevokedAction}"));

        await AssertRefreshAsync(signed, HttpStatusCode.OK);
    }
}
