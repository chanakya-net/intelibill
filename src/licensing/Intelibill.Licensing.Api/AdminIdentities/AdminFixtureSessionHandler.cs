using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Api.AdminIdentities;

/// <summary>
/// LOCAL FIXTURE ONLY. Registered solely in Development/Testing when a fixture digest is
/// configured; startup refuses the configuration anywhere else. It names a principal, but
/// handlers still require that principal's persisted owner authority and recent MFA.
/// </summary>
internal sealed class AdminFixtureOptions
{
    internal const string SectionName = "AdminFixtures";

    public string? SessionDigest { get; init; }
    public Guid? AdminId { get; init; }
    public string? Capabilities { get; init; }

    /// <summary>The asserted MFA time; absent means "just verified" for each request.</summary>
    public DateTimeOffset? MfaVerifiedAt { get; init; }

    internal bool IsConfigured => !string.IsNullOrEmpty(SessionDigest);

    internal bool IsValid() =>
        SessionDigest is { Length: 64 } && SessionDigest.All(char.IsAsciiHexDigitUpper) && AdminId is not null;
}

internal sealed class AdminFixtureSessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IOptions<AdminFixtureOptions> fixture, TimeProvider clock, IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string FixtureCookieName = "Intelibill.Admin.FixtureSession";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var settings = fixture.Value;
        var presented = Request.Cookies[FixtureCookieName];
        if (AdminSignInHttp.HasAccess(Context) || !settings.IsConfigured || !settings.IsValid() || !Bootstrap.BootstrapHttp.AllowsLoopbackHttp(environment)
            || presented is not { Length: > 0 and <= 256 }
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
                Convert.FromHexString(settings.SessionDigest!)))
            return Task.FromResult(AuthenticateResult.NoResult());

        var mfa = settings.MfaVerifiedAt ?? clock.GetUtcNow();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, settings.AdminId!.Value.ToString()),
            new(ProviderSessions.MfaVerifiedAtClaim, mfa.ToString("O", CultureInfo.InvariantCulture)),
        };
        foreach (var capability in (settings.Capabilities ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new Claim(ProviderSessions.CapabilityClaim, capability));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
