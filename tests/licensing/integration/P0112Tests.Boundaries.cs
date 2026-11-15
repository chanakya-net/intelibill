using System.Net;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0112Tests
{
    [Fact]
    public async Task RecoveryRequestsAreOriginBoundSizeBoundedAndThrottled()
    {
        await using var fixture = await StartAsync();
        var request = new { email = OwnerEmail, password = "Wrong-password-0000", recoveryCode = fixture.Codes[5] };
        using var anonymous = fixture.Browser();
        using var noCsrf = await anonymous.SendAsync(HttpMethod.Post, RecoveryCode, request);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var foreign = await PostAsync(anonymous, RecoveryCode, request, "https://other.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var oversized = await PostAsync(anonymous, RecoveryCode,
            new { email = OwnerEmail, password = new string('x', 5000), recoveryCode = fixture.Codes[5] });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM provider_audit_events WHERE action = 22"));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var refused = await PostAsync(anonymous, RecoveryCode, request);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        // The account's budget is spent: even the right password and an unused code wait.
        using var throttled = await PostAsync(anonymous, RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[5] });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.True(throttled.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM admin_recovery_codes WHERE consumed_at IS NOT NULL"));
        Assert.Equal(HttpStatusCode.OK, await fixture.SignInStatusAsync(OwnerEmail, fixture.Password, fixture.Seed));

        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        using var recovered = await PostAsync(fixture.Browser(), RecoveryCode,
            new { email = OwnerEmail, password = fixture.Password, recoveryCode = fixture.Codes[5] });
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }
}
