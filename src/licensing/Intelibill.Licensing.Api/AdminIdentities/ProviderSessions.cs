using System.Globalization;
using System.Security.Claims;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Microsoft.AspNetCore.Authentication;

namespace Intelibill.Licensing.Api.AdminIdentities;

/// <summary>
/// The provider session that sensitive admin routes require. Its production producer (TOTP
/// sign-in) arrives with P01-08/P01-09; until then the scheme authenticates nobody, so every
/// route behind it fails closed. Development and Testing may substitute the guarded fixture.
/// </summary>
internal static class ProviderSessions
{
    internal const string Scheme = "ProviderSession";
    internal const string InvitationPolicy = "ProviderInvitations";
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
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ProviderSessionHandler>(Scheme, _ => { });

        services.AddAuthorizationBuilder().AddPolicy(InvitationPolicy, policy => policy
            .AddAuthenticationSchemes(Scheme)
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
