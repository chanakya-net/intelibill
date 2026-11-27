using System.IdentityModel.Tokens.Jwt;

namespace Intelibill.Integration.Tests;

/// <summary>
/// The issuer the web client stamps on every offline invoice: the user signed in when it was issued.
/// </summary>
internal static class OfflineIssuer
{
    public static Guid Of(string accessToken) =>
        Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Subject);
}
