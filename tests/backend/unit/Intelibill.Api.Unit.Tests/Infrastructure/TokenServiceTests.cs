using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;
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

    private static User CreateUser()
    {
        var user = User.CreateWithEmail("ada@example.com", "hashed-password", "Ada", "Lovelace", "9876500001");
        user.VerifyEmail("ada@example.com");
        return user;
    }

    private static AuthSession CreateSession(User user, Guid? activeShopId = null) =>
        AuthSession.Start(user.Id, user.SecurityVersion, "1", activeShopId, DeviceMetadata.Unknown, Now, Now.AddDays(7));

    [Fact]
    public async Task GenerateAccessTokenAsync_ProducesATokenTheStandardHandlerAccepts()
    {
        // The token is assembled by hand rather than by JwtSecurityTokenHandler,
        // so the thing worth proving is that the handler still validates it.
        var (token, _) = await CreateService().GenerateAccessTokenAsync(UserAndSession(out var session), session, null);

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
        var (token, _) = await CreateService().GenerateAccessTokenAsync(UserAndSession(out var session), session, null);

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
        var user = CreateUser();
        var session = CreateSession(user, shopId);

        var (token, expiresAt) = await CreateService()
            .GenerateAccessTokenAsync(user, session, "Owner");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(shopId.ToString(), jwt.Claims.Single(c => c.Type == "active_shop_id").Value);
        Assert.Equal("Owner", jwt.Claims.Single(c => c.Type == "active_shop_role").Value);
        Assert.Equal(Now.AddMinutes(15), expiresAt);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_OmitsKidWhenTheSignerHasNoKeyToIdentify()
    {
        var (token, _) = await CreateService().GenerateAccessTokenAsync(UserAndSession(out var session), session, null);

        Assert.Null(new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_AdvertisesTheSignersKeyId()
    {
        // Validation resolves the public key by kid, so a token that omits it is
        // unverifiable the moment a second key version exists.
        var (token, _) = await CreateService(new StubSigner("RS256", "key-version-2"))
            .GenerateAccessTokenAsync(UserAndSession(out var session), session, null);

        var header = new JwtSecurityTokenHandler().ReadJwtToken(token).Header;

        Assert.Equal("RS256", header.Alg);
        Assert.Equal("key-version-2", header.Kid);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_SignsTheHeaderAndPayloadItSends()
    {
        var signer = new StubSigner("RS256", "key-version-2");

        var (token, _) = await CreateService(signer).GenerateAccessTokenAsync(UserAndSession(out var session), session, null);

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

    private static User UserAndSession(out AuthSession session)
    {
        var user = CreateUser();
        session = CreateSession(user);
        return user;
    }


    /// <summary>
    /// The session id is what makes a token revocable on its own, and the scope version is what
    /// stops a token naming a shop the session has since left.
    /// </summary>
    [Fact]
    public async Task GenerateAccessTokenAsync_NamesTheSessionAndItsScopeVersion()
    {
        var shopId = Guid.NewGuid();
        var user = CreateUser();
        var session = CreateSession(user, shopId);
        session.ChangeScope(Guid.NewGuid(), Now);

        var (token, _) = await CreateService().GenerateAccessTokenAsync(user, session, "Owner");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(session.Id.ToString(), jwt.Claims.Single(c => c.Type == "sid").Value);
        Assert.Equal("1", jwt.Claims.Single(c => c.Type == "scope_version").Value);
        Assert.Equal(session.ActiveShopId!.Value.ToString(), jwt.Claims.Single(c => c.Type == "active_shop_id").Value);
    }

    /// <summary>
    /// The shop claim is taken from the session row rather than from a caller-supplied argument, so
    /// a token can never name a shop the session is not scoped to.
    /// </summary>
    [Fact]
    public async Task GenerateAccessTokenAsync_OmitsShopClaimsWhenTheSessionHasNoShop()
    {
        var user = CreateUser();
        var session = CreateSession(user);

        var (token, _) = await CreateService().GenerateAccessTokenAsync(user, session, "Owner");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == "active_shop_id");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "active_shop_role");
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
