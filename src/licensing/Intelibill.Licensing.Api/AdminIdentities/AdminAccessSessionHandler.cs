using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal sealed class AdminAccessSessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    AdminAccessTokens tokens, AdminSignInHttp http, IAdminSessionStore sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AdminSignInHttp.HasAccess(Context)) return AuthenticateResult.NoResult();
        var origin = await http.ReadOriginAsync(Context);
        if (origin is null || tokens.Read(AdminSignInHttp.ReadAccess(Context, origin)) is not { } proof)
            return AuthenticateResult.NoResult();

        var access = await sessions.AuthorizeAsync(proof.AdminId, proof.SessionId, proof.ExpiresAt, Context.RequestAborted);
        if (access is null) return AuthenticateResult.NoResult();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, access.AdminId.ToString()),
            new(ProviderSessions.SessionIdClaim, proof.SessionId.ToString()),
            new(ProviderSessions.AccessExpiresClaim, proof.ExpiresAt.ToString("O", CultureInfo.InvariantCulture)),
            new(ProviderSessions.MfaVerifiedAtClaim, access.MfaVerifiedAt.ToString("O", CultureInfo.InvariantCulture)),
            new(ProviderSessions.BusinessClaim, "true"),
        };
        foreach (var capability in access.Capabilities)
            claims.Add(new Claim(ProviderSessions.CapabilityClaim, capability));
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
    }
}
