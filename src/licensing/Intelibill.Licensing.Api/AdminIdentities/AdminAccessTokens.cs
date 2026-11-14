using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Intelibill.Licensing.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal sealed class AdminSessionOptions
{
    public string? SigningKey { get; init; }

    internal bool IsValid() => string.IsNullOrEmpty(SigningKey) || DecodeValidKey(SigningKey);

    internal SymmetricSecurityKey? DeriveKey()
    {
        if (string.IsNullOrEmpty(SigningKey)) return null;
        var root = Convert.FromBase64String(SigningKey);
        try
        {
            return new SymmetricSecurityKey(HKDF.DeriveKey(HashAlgorithmName.SHA256, root, 32,
                info: "intelibill-admin/access-signing/v1"u8.ToArray()));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
        }
    }

    private static bool DecodeValidKey(string value)
    {
        Span<byte> decoded = stackalloc byte[32];
        var valid = Convert.TryFromBase64String(value, decoded, out var length) && length == decoded.Length;
        CryptographicOperations.ZeroMemory(decoded);
        return valid;
    }
}

internal sealed class AdminAccessTokens(IOptions<AdminSessionOptions> options, TimeProvider clock)
{
    private const string Issuer = "InteliBill.Licensing.Admin";
    private const string Audience = "InteliBill.ProviderConsole";
    private const string TokenType = "intelibill-admin-access+jwt";
    private const string SessionClaim = "sid";
    private readonly SymmetricSecurityKey? _key = options.Value.DeriveKey();
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false, MaximumTokenSizeInBytes = 2048 };

    internal bool IsConfigured => _key is not null;

    internal string Create(AdminSession session)
    {
        var issued = session.MfaVerifiedAt.UtcDateTime;
        var token = new JwtSecurityToken(Issuer, Audience,
            [new(JwtRegisteredClaimNames.Sub, session.AdminId.ToString()), new(SessionClaim, session.Id.ToString())],
            issued, issued + AdminSession.AccessLifetime, new SigningCredentials(_key!, SecurityAlgorithms.HmacSha256));
        token.Header[JwtHeaderParameterNames.Typ] = TokenType;
        return _handler.WriteToken(token);
    }

    internal AdminAccessProof? Read(string? raw)
    {
        if (_key is null || raw is not { Length: > 0 and <= 2048 } || !_handler.CanReadToken(raw)) return null;
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            IssuerSigningKey = _key,
            ValidateIssuerSigningKey = true,
            ValidTypes = [TokenType],
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = IsCurrent,
        };

        try
        {
            var principal = _handler.ValidateToken(raw, parameters, out var token);
            return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var adminId)
                && Guid.TryParse(principal.FindFirstValue(SessionClaim), out var sessionId)
                ? new AdminAccessProof(adminId, sessionId, new DateTimeOffset(token.ValidTo)) : null;
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    private bool IsCurrent(DateTime? issued, DateTime? expiry, SecurityToken token, TokenValidationParameters parameters)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return issued <= now && expiry > now && expiry - issued == AdminSession.AccessLifetime;
    }
}

internal sealed record AdminAccessProof(Guid AdminId, Guid SessionId, DateTimeOffset ExpiresAt);
