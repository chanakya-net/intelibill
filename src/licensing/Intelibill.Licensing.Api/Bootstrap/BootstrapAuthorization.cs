using System.Security.Claims;
using System.Text.Encodings.Web;
using Intelibill.Licensing.Application.Bootstrap;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Api.Bootstrap;

internal static class BootstrapAuthorization
{
    internal const string EnrollmentScheme = "BootstrapEnrollment";
    internal const string BusinessPolicy = "ProviderBusinessAccess";
    internal const string ExpiryClaim = "bootstrap_enrollment_expiry";

    internal static void AddBootstrapAuthorization(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BootstrapEnrollmentAuthenticationHandler>(
            EnrollmentScheme, _ => { });
        services.AddAuthorizationBuilder()
            .AddPolicy(EnrollmentScheme, policy => policy
                .AddAuthenticationSchemes(EnrollmentScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(BusinessPolicy, policy => policy
                .AddAuthenticationSchemes(EnrollmentScheme)
                // No business scheme issues authenticated grants in this phase slice.
                .RequireAuthenticatedUser()
                .RequireClaim("provider_business_session"));
    }
}

internal sealed class BootstrapEnrollmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IBootstrapStore store, IBootstrapSecrets secrets, TimeProvider clock, IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return AuthenticateResult.NoResult();

        var credential = Request.Cookies[BootstrapHttp.CookieName];
        if (!BootstrapValidation.IsEnrollmentValid(credential))
            return AuthenticateResult.NoResult();

        var state = await store.ReadAsync(Context.RequestAborted);
        if (!BootstrapHttp.MatchesHost(Context, state.AdminOrigin, environment))
            return AuthenticateResult.NoResult();

        var enrollment = await store.FindEnrollmentAsync(secrets.HashEnrollment(credential!), clock.GetUtcNow(), Context.RequestAborted);
        if (enrollment is null) return AuthenticateResult.NoResult();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, enrollment.OwnerId.ToString()),
            new Claim(BootstrapAuthorization.ExpiryClaim, enrollment.ExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
