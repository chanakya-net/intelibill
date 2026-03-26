using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using InventoryAI.Application.Common.Interfaces;
using InventoryAI.Application.Common.Models;
using InventoryAI.Domain.Enums;
using InventoryAI.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace InventoryAI.Infrastructure.Services.Auth.ExternalAuth;

internal sealed class AppleAuthProvider(IOptions<ExternalAuthOptions> options) : IExternalAuthProvider
{
    private readonly ExternalAuthOptions.AppleOptions _apple = options.Value.Apple;

    public ExternalAuthProvider Provider => ExternalAuthProvider.Apple;

    public async Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            const string discoveryEndpoint = "https://appleid.apple.com/.well-known/openid-configuration";

            var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                discoveryEndpoint,
                new OpenIdConnectConfigurationRetriever());

            var oidcConfig = await configManager.GetConfigurationAsync(cancellationToken);

            var validationParams = new TokenValidationParameters
            {
                ValidIssuer = "https://appleid.apple.com",
                ValidAudience = _apple.ClientId,
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParams, out _);

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? string.Empty;

            var email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                        ?? principal.FindFirst(ClaimTypes.Email)?.Value;

            // Apple only sends the user's name on the very first sign-in via the front-end payload.
            // The identity_token itself does not carry name claims; the caller must forward them.
            return new ExternalUserInfo(
                ProviderKey: sub,
                Email: email,
                FirstName: string.Empty,
                LastName: string.Empty);
        }
        catch (SecurityTokenException ex)
        {
            return Error.Unauthorized("Auth.Apple.InvalidToken", ex.Message);
        }
    }
}
