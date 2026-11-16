using System.Globalization;
using System.Security.Claims;
using Intelibill.Licensing.Api.Bootstrap;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Catalogue;
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
    internal const string DevicePolicy = "ProviderDeviceSessions";
    internal const string AuditPolicy = "ProviderIdentityAudit";
    internal const string CapabilityPolicy = "ProviderCapabilityManagement";
    internal const string RecoveryPolicy = "ProviderRecoveryReview";
    internal const string CataloguePolicy = "ProviderCatalogueDrafts";
    internal const string StatusPolicy = "ProviderSubscriptionStatus";
    internal const string GrantPolicy = "ProviderComplimentaryGrants";
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
        services.AddScoped<AdminRecoveryHandler>();
        services.AddScoped<UpdateAdminCapabilitiesHandler>();
        services.AddScoped<SavePlanDraftHandler>();
        services.AddScoped<SaveTrialPolicyDraftHandler>();
        services.AddScoped<PublishTrialPolicyVersionHandler>();
        services.AddScoped<PublishPlanVersionHandler>();
        services.AddScoped<RetirePlanHandler>();
        services.AddScoped<CataloguePublicationOperation>();
        services.AddScoped<ReadCatalogueHistoryHandler>();
        services.AddScoped<AdminDeviceSessions>();
        services.AddScoped<AdminSignOutHandler>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AdminAccessSessionHandler>(Scheme, _ => { })
            .AddScheme<AuthenticationSchemeOptions, AdminFixtureSessionHandler>(FixtureScheme, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(DevicePolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(BusinessClaim, "true"))
            .AddPolicy(AuditPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(BusinessClaim, "true")
                .RequireClaim(CapabilityClaim, ProviderActor.AuditIdentityReadCapability))
            .AddPolicy(CapabilityPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(BusinessClaim, "true")
                .RequireClaim(CapabilityClaim, ProviderActor.AccessManagementCapability))
            // The Licensing Manager grant is re-checked against the live identity inside each transaction.
            .AddPolicy(CataloguePolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(BusinessClaim, "true"))
            // Status capability (Support, Billing or Licensing Manager) is re-checked against the live identity.
            .AddPolicy(StatusPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(BusinessClaim, "true"))
            // Complimentary grants: the Licensing Manager grant and recent MFA are re-checked against live authority.
            .AddPolicy(GrantPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(BusinessClaim, "true"))
            .AddPolicy(RecoveryPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(BusinessClaim, "true")
                .RequireClaim(CapabilityClaim, ProviderActor.RecoveryReviewCapability))
            .AddPolicy(InvitationPolicy, policy => policy
            .AddAuthenticationSchemes(Scheme, FixtureScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(CapabilityClaim, ProviderActor.InvitationCapability));
    }

    internal static AdminSessionCaller? ReadCaller(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)
        && Guid.TryParse(user.FindFirstValue(SessionIdClaim), out var sessionId)
        && DateTimeOffset.TryParse(user.FindFirstValue(AccessExpiresClaim), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var expires)
            ? new(adminId, sessionId, expires) : null;

    internal static ProviderActor? ToActor(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId)
        && DateTimeOffset.TryParse(user.FindFirstValue(MfaVerifiedAtClaim), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var mfaVerifiedAt)
            ? new ProviderActor(adminId, mfaVerifiedAt, ReadCaller(user))
            : null;
}
