using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Shared.Web.PublicErrors;

namespace Intelibill.Licensing.Api.Bootstrap;

internal static class BootstrapHttp
{
    internal const string Path = "/api/admin/auth/bootstrap";
    internal const string CsrfCookieName = "Intelibill.Admin.BootstrapCsrf";
    internal const string CookieName = "Intelibill.Admin.BootstrapEnrollment";

    internal static bool AllowsLoopbackHttp(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    internal static bool MatchesHost(HttpContext context, string? origin, IHostEnvironment environment)
    {
        if (!BootstrapValidation.IsOriginAllowed(origin, AllowsLoopbackHttp(environment))) return false;
        var uri = new Uri(origin!);
        // The browser authority is preserved by the admin proxy. Cookie security is
        // based on the prepared origin and environment, not forwarded scheme headers.
        return string.Equals(context.Request.Host.Value, uri.Authority, StringComparison.OrdinalIgnoreCase);
    }

    internal static CookieOptions Cookie(string origin, string path) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = origin.StartsWith("https://", StringComparison.Ordinal),
        Path = path,
    };

    /// <summary>Sets the MFA-pending enrollment credential; it confers no business access.</summary>
    internal static IResult Enrollment(HttpContext context, string origin, EnrollmentGrant grant)
    {
        var cookie = Cookie(origin, "/api/admin");
        cookie.MaxAge = TimeSpan.FromMinutes(15);
        context.Response.Cookies.Append(CookieName, grant.Credential.Value, cookie);
        return EnrollmentResponse(grant.ExpiresAt);
    }

    internal static IResult EnrollmentResponse(DateTimeOffset expiry) =>
        Results.Ok(new { state = "enrollmentRequired", expiresAt = expiry });

    internal static IResult Throttled(HttpContext context, DateTimeOffset retryAt, DateTimeOffset now, string code)
    {
        context.Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling((retryAt - now).TotalSeconds))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Problem(context, 429, code, "Too many attempts. Try again later.");
    }

    internal static IResult Problem(HttpContext context, int status, string code, string title) =>
        Results.Json(new PublicError(status, code, title, context.TraceIdentifier),
            statusCode: status, contentType: "application/problem+json");
}
