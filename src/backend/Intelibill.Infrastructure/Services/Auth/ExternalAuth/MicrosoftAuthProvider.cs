using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Models;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Infrastructure.Services.Auth.ExternalAuth;

internal sealed class MicrosoftAuthProvider(IOptions<ExternalAuthOptions> options) : IExternalAuthProvider
{
    private readonly ExternalAuthOptions.MicrosoftOptions _ms = options.Value.Microsoft;

    public ExternalAuthProvider Provider => ExternalAuthProvider.Microsoft;

    public async Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var discoveryEndpoint =
                $"https://login.microsoftonline.com/{_ms.TenantId}/v2.0/.well-known/openid-configuration";

            var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                discoveryEndpoint,
                new OpenIdConnectConfigurationRetriever());

            var oidcConfig = await configManager.GetConfigurationAsync(cancellationToken);

            var validationParams = new TokenValidationParameters
            {
                ValidIssuer = $"https://login.microsoftonline.com/{_ms.TenantId}/v2.0",
                ValidAudience = _ms.ClientId,
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParams, out _);

            var sub = principal.FindFirst("oid")?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? string.Empty;

            var email = principal.FindFirst("preferred_username")?.Value
                        ?? principal.FindFirst(ClaimTypes.Email)?.Value;

            var firstName = principal.FindFirst(ClaimTypes.GivenName)?.Value ?? string.Empty;
            var lastName = principal.FindFirst(ClaimTypes.Surname)?.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(firstName))
            {
                var name = principal.FindFirst("name")?.Value ?? string.Empty;
                var parts = name.Split(' ', 2);
                firstName = parts.Length > 0 ? parts[0] : string.Empty;
                lastName = parts.Length > 1 ? parts[1] : string.Empty;
            }

            return new ExternalUserInfo(sub, email, firstName, lastName);
        }
        catch (SecurityTokenException ex)
        {
            return Error.Unauthorized("Auth.Microsoft.InvalidToken", ex.Message);
        }
    }
}
