using System.Security.Cryptography;
using System.Text;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal sealed class AdminSignInHttp(IAdminInvitationStore provider, IHostEnvironment environment,
    IDataProtectionProvider protection)
{
    internal const string CsrfHeader = "X-Admin-CSRF";
    private const string AccessSuffix = "InteliBill.Admin.Access";
    private const string CsrfSuffix = "InteliBill.Admin.Csrf";
    private readonly IDataProtector _csrf = protection.CreateProtector("InteliBill.Licensing.Admin.SignInCsrf.v1");

    internal async Task<string?> ReadOriginAsync(HttpContext context) =>
        await provider.FindProviderOriginAsync(context.RequestAborted) is { } origin
            && BootstrapHttp.MatchesHost(context, origin, environment) ? origin : null;

    internal string IssueCsrf(HttpContext context, string origin)
    {
        var name = CookieName(origin, CsrfSuffix);
        if (context.Request.Cookies[name] is not { Length: 43 } nonce)
        {
            nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            context.Response.Cookies.Append(name, nonce, Cookie(origin));
        }
        return _csrf.Protect(nonce);
    }

    internal bool AcceptsMutation(HttpContext context, string origin)
    {
        if (!string.Equals(context.Request.Headers.Origin.ToString(), origin, StringComparison.Ordinal)
            || context.Request.Cookies[CookieName(origin, CsrfSuffix)] is not { Length: 43 } nonce
            || context.Request.Headers[CsrfHeader].ToString() is not { Length: > 0 and <= 512 } proof)
            return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(nonce), Encoding.UTF8.GetBytes(_csrf.Unprotect(proof)));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    internal static string? ReadAccess(HttpContext context, string origin) => context.Request.Cookies[CookieName(origin, AccessSuffix)];

    internal static bool HasAccess(HttpContext context) =>
        context.Request.Cookies.ContainsKey("__Host-" + AccessSuffix) || context.Request.Cookies.ContainsKey(AccessSuffix);

    internal static void WriteAccess(HttpContext context, string origin, string value, TimeSpan duration)
    {
        var cookie = Cookie(origin);
        cookie.MaxAge = duration;
        context.Response.Cookies.Append(CookieName(origin, AccessSuffix), value, cookie);
    }

    private static CookieOptions Cookie(string origin) =>
        BootstrapHttp.Cookie(origin, IsHttps(origin) ? "/" : "/api/admin");

    private static string CookieName(string origin, string suffix) => IsHttps(origin) ? "__Host-" + suffix : suffix;

    private static bool IsHttps(string origin) => origin.StartsWith("https://", StringComparison.Ordinal);
}
