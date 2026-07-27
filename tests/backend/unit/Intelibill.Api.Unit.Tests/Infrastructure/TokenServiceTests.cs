using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Services.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Intelibill.Api.Unit.Tests.Infrastructure;

public class TokenServiceTests
{
    private const string Secret = "unit-test-signing-secret-at-least-32-chars";

    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions Options = new()
    {
        Issuer = "Intelibill",
        Audience = "Intelibill",
        Secret = Secret,
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays = 7,
    };

    private static TokenService CreateService(IJwtSigner? signer = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options);
        var timeProvider = new FakeTimeProvider(Now);

        return new TokenService(options, signer ?? new HmacJwtSigner(options), timeProvider);
    }

    private static User CreateUser() =>
        User.CreateWithEmail("Ada", "Lovelace", "ada@example.com", "hashed-password", "9876500001");

    [Fact]
    public async Task GenerateAccessTokenAsync_ProducesATokenTheStandardHandlerAccepts()
    {
        // The token is assembled by hand rather than by JwtSecurityTokenHandler,
        // so the thing worth proving is that the handler still validates it.
        var (token, _) = await CreateService().GenerateAccessTokenAsync(CreateUser());

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (_, expires, _, _) => expires > Now.UtcDateTime,
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out var validated);

        Assert.NotNull(principal);
        Assert.Equal("HS256", ((JwtSecurityToken)validated).Header.Alg);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_RejectsATokenSignedWithAnotherSecret()
    {
        var (token, _) = await CreateService().GenerateAccessTokenAsync(CreateUser());

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 40))),
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            ValidateLifetime = false,
        };

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(
            () => new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _));
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_CarriesTheShopClaimsWhenSupplied()
    {
        var shopId = Guid.NewGuid();

        var (token, expiresAt) = await CreateService()
            .GenerateAccessTokenAsync(CreateUser(), shopId, "Owner");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(shopId.ToString(), jwt.Claims.Single(c => c.Type == "active_shop_id").Value);
        Assert.Equal("Owner", jwt.Claims.Single(c => c.Type == "active_shop_role").Value);
        Assert.Equal(Now.AddMinutes(15), expiresAt);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_OmitsShopClaimsWhenThereIsNoActiveShop()
    {
        var (token, _) = await CreateService().GenerateAccessTokenAsync(CreateUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == "active_shop_id");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "active_shop_role");
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_OmitsKidWhenTheSignerHasNoKeyToIdentify()
    {
        var (token, _) = await CreateService().GenerateAccessTokenAsync(CreateUser());

        Assert.Null(new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_AdvertisesTheSignersKeyId()
    {
        // Validation resolves the public key by kid, so a token that omits it is
        // unverifiable the moment a second key version exists.
        var (token, _) = await CreateService(new StubSigner("RS256", "key-version-2"))
            .GenerateAccessTokenAsync(CreateUser());

        var header = new JwtSecurityTokenHandler().ReadJwtToken(token).Header;

        Assert.Equal("RS256", header.Alg);
        Assert.Equal("key-version-2", header.Kid);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_SignsTheHeaderAndPayloadItSends()
    {
        var signer = new StubSigner("RS256", "key-version-2");

        var (token, _) = await CreateService(signer).GenerateAccessTokenAsync(CreateUser());

        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Equal($"{parts[0]}.{parts[1]}", Encoding.ASCII.GetString(signer.LastSigningInput!));
    }

    [Fact]
    public void CreateRefreshToken_ExpiresOnTheConfiguredSchedule()
    {
        var refreshToken = CreateService().CreateRefreshToken(Guid.NewGuid());

        Assert.Equal(Now.AddDays(7), refreshToken.ExpiresAt);
    }

    private sealed class StubSigner(string algorithm, string? keyId) : IJwtSigner
    {
        public byte[]? LastSigningInput { get; private set; }

        public string Algorithm => algorithm;

        public ValueTask<string?> GetKeyIdAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(keyId);

        public ValueTask<byte[]> SignAsync(byte[] signingInput, CancellationToken cancellationToken = default)
        {
            LastSigningInput = signingInput;
            return ValueTask.FromResult(new byte[] { 1, 2, 3, 4 });
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
