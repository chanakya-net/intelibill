using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Intelibill.Shared.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Intelibill.P0103ConsumerRegressionTests;

public sealed partial class ConsumerTests
{
    [Fact]
    public async Task LicensingJwtCannotAuthorizeMerchantRequests()
    {
        var merchant = await database.SeedMerchantAsync(withShop: true);
        using var host = new MerchantHost(database.MerchantRuntime);
        using var licensingHost = new LicensingHost(database.LicensingRuntime);
        using var licensingClient = SecurityFixtures.HttpsClient(licensingHost);
        using var readiness = await licensingClient.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        using var client = SecurityFixtures.HttpsClient(host);
        var auth = await LoginAsync(client, merchant.Email!);
        Assert.Equal(HttpStatusCode.OK, await ProtectedStatusAsync(client, auth.AccessToken));

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken).Claims
            .Where(claim => claim.Type is not ("iss" or "aud" or "nbf" or "exp")).ToArray();
        Assert.NotNull(auth.ActiveShopId);
        Assert.Contains(claims, claim => claim.Type == "active_shop_id" && claim.Value == auth.ActiveShopId.ToString());
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var merchantControl = SignAndValidate(SecurityFixtures.MerchantIssuer, SecurityFixtures.MerchantAudience,
            SecurityFixtures.MerchantKey, claims, now);
        Assert.Equal(HttpStatusCode.OK, await ProtectedStatusAsync(client, merchantControl));

        var licensingToken = SignAndValidate(SecurityFixtures.LicensingIssuer, SecurityFixtures.LicensingAudience,
            SecurityFixtures.LicensingKey, claims, now);
        Assert.Equal(HttpStatusCode.Unauthorized, await ProtectedStatusAsync(client, licensingToken));

        foreach (var (issuer, audience, key) in SingleMismatches())
        {
            var token = SignAndValidate(issuer, audience, key, claims, now);
            Assert.Equal(HttpStatusCode.Unauthorized, await ProtectedStatusAsync(client, token));
        }

        var opaque = licensingHost.Services.GetRequiredService<OpaqueTokenPrimitives>().Create();
        using var refresh = await RefreshAsync(client, opaque.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await ProtectedStatusAsync(client, auth.AccessToken));
    }

    [Fact]
    public async Task RuntimeRolesCannotConnectToEachOthersDatabases()
    {
        await AssertCrossDatabaseRefusedAsync(database.MerchantRuntime, database.LicensingRuntime.Database!);
        await AssertCrossDatabaseRefusedAsync(database.LicensingRuntime, database.MerchantRuntime.Database!);
    }

    private static async Task AssertCrossDatabaseRefusedAsync(NpgsqlConnectionStringBuilder runtime, string otherDatabase)
    {
        await using var ownConnection = new NpgsqlConnection(runtime.ConnectionString);
        await ownConnection.OpenAsync();

        var foreignSettings = new NpgsqlConnectionStringBuilder(runtime.ConnectionString) { Database = otherDatabase };
        await using var foreignConnection = new NpgsqlConnection(foreignSettings.ConnectionString);
        var error = await Assert.ThrowsAsync<PostgresException>(() => foreignConnection.OpenAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    private static (string Issuer, string Audience, string Key)[] SingleMismatches() =>
    [
        (SecurityFixtures.LicensingIssuer, SecurityFixtures.MerchantAudience, SecurityFixtures.MerchantKey),
        (SecurityFixtures.MerchantIssuer, SecurityFixtures.LicensingAudience, SecurityFixtures.MerchantKey),
        (SecurityFixtures.MerchantIssuer, SecurityFixtures.MerchantAudience, SecurityFixtures.LicensingKey),
    ];

    private static string SignAndValidate(string issuer, string audience, string secret,
        IEnumerable<Claim> claims, DateTime now)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(new JwtSecurityToken(issuer, audience, claims, now, now.AddMinutes(5),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

        handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = key,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        }, out _);
        return token;
    }
}
