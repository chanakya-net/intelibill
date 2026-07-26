using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Infrastructure.Services.Auth;

internal sealed class TokenService(IOptions<JwtOptions> options, IJwtSigner signer, TimeProvider timeProvider)
    : ITokenService
{
    private readonly JwtOptions _jwt = options.Value;
    private const string ActiveShopClaim = "active_shop_id";
    private const string ActiveShopRoleClaim = "active_shop_role";
    private const string LanguageClaim = "language";

    public async Task<(string AccessToken, DateTimeOffset ExpiresAt)> GenerateAccessTokenAsync(
        User user,
        Guid? activeShopId = null,
        string? activeShopRole = null,
        CancellationToken cancellationToken = default)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(_jwt.AccessTokenExpiryMinutes);

        // Assembled by hand rather than through JwtSecurityTokenHandler, whose
        // signing path is synchronous: Key Vault signing is a network call, and
        // blocking a request thread on one is how a thread pool starves.
        var header = new JsonObject
        {
            ["alg"] = signer.Algorithm,
            ["typ"] = "JWT",
        };

        // Resolved before the header is serialised, because kid is part of the
        // signed input. It is also what lets validation pick the right public key
        // after a rotation.
        var keyId = await signer.GetKeyIdAsync(cancellationToken).ConfigureAwait(false);
        if (keyId is not null)
        {
            header["kid"] = keyId;
        }

        var payload = new JsonObject
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Iss] = _jwt.Issuer,
            [JwtRegisteredClaimNames.Aud] = _jwt.Audience,
            [JwtRegisteredClaimNames.Iat] = issuedAt.ToUnixTimeSeconds(),
            [JwtRegisteredClaimNames.Nbf] = issuedAt.ToUnixTimeSeconds(),
            [JwtRegisteredClaimNames.Exp] = expiresAt.ToUnixTimeSeconds(),
            [LanguageClaim] = user.Language,
        };

        if (user.Email is not null)
        {
            payload[JwtRegisteredClaimNames.Email] = user.Email;
        }

        if (activeShopId is not null)
        {
            payload[ActiveShopClaim] = activeShopId.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(activeShopRole))
        {
            payload[ActiveShopRoleClaim] = activeShopRole;
        }

        var signingInput = string.Concat(
            Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(header)),
            ".",
            Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(payload)));

        var signature = await signer
            .SignAsync(Encoding.ASCII.GetBytes(signingInput), cancellationToken)
            .ConfigureAwait(false);

        return (string.Concat(signingInput, ".", Base64UrlEncoder.Encode(signature)), expiresAt);
    }

    public RefreshToken CreateRefreshToken(Guid userId)
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToBase64String(bytes);
        var expiresAt = timeProvider.GetUtcNow().AddDays(_jwt.RefreshTokenExpiryDays);

        return RefreshToken.Create(userId, token, expiresAt);
    }
}
