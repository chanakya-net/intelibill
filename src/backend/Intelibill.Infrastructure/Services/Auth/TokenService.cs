using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Infrastructure.Services.Auth;

internal sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _jwt = options.Value;

    public (string AccessToken, DateTimeOffset ExpiresAt) GenerateAccessToken(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_jwt.AccessTokenExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        };

        if (user.Email is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public RefreshToken CreateRefreshToken(Guid userId)
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToBase64String(bytes);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays);

        return RefreshToken.Create(userId, token, expiresAt);
    }
}
