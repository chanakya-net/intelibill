using System.Globalization;
using System.Security.Claims;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.AspNetCore.Authentication;

namespace Intelibill.Licensing.Api.AdminIdentities;

internal static class ProviderSessions
{
    internal const string Scheme = "ProviderSession";
    internal const string FixtureScheme = "ProviderLocalFixture";
    internal const string BusinessClaim = "provider_business_session";
    internal const string SessionIdClaim = "provider_session_id";
    internal const string AccessExpiresClaim = "provider_access_expires";
    internal const string InvitationPolicy = "ProviderInvitations";
    internal const string AuditPolicy = "ProviderIdentityAudit";
    internal const string CapabilityClaim = "provider_capability";
    internal const string MfaVerifiedAtClaim = "provider_mfa_at";

    internal static void AddProviderSessions(
        this IServiceCollection services, IHostEnvironment environment)
    {
        // A configured fixture outside Development/Testing stops the host before it serves.
        services.AddOptions<AdminFixtureOptions>()
            .BindConfiguration(AdminFixtureOptions.SectionName)
            .Validate(fixture => !fixture.IsConfigured || (BootstrapHttp.AllowsLoopbackHttp(environment) && fixture.IsValid()),
                "Admin fixture sessions are refused in this environment or configuration.")
            .ValidateOnStart();
        services.AddOptions<AdminSessionOptions>().BindConfiguration("AdminSessions")
            .Validate(settings => settings.IsValid(),
                "The provider session signing key must decode to 32 bytes.").ValidateOnStart();
        services.AddSingleton<AdminAccessTokens>();
        services.AddScoped<AdminSignInHttp>();
        services.AddScoped<AdminSignInHandler>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AdminAccessSessionHandler>(Scheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, AdminFixtureSessionHandler>(FixtureScheme, _ => { });

        services.AddAuthorizationBuilder().AddPolicy(AuditPolicy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(BusinessClaim, "true")
            .RequireClaim(CapabilityClaim, ProviderActor.AuditIdentityReadCapability))
            .AddPolicy(InvitationPolicy, policy => policy
            .AddAuthenticationSchemes(Scheme, FixtureScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(CapabilityClaim, ProviderActor.InvitationCapability));
    }

    internal static ProviderActor? ToActor(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)
        && DateTimeOffset.TryParse(user.FindFirstValue(MfaVerifiedAtClaim), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var mfaVerifiedAt)
            ? new ProviderActor(adminId, mfaVerifiedAt)
            : null;
}
