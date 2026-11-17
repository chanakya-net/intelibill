using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Payments.Development;
using Intelibill.Licensing.Infrastructure.Payments.Razorpay;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Intelibill.Licensing.Infrastructure.Payments;

/// <summary>Retained immutable profile references. Secret values never enter the routing API or database.</summary>
internal sealed class PaymentProviderProfile
{
    public string Id { get; init; } = "";
    public string ProviderId { get; init; } = "";
    public string AccountId { get; init; } = "";
    public string Environment { get; init; } = "";
    public string ConfigurationVersion { get; init; } = "";
    public string? CredentialReference { get; init; }
    public string? WebhookSecretReference { get; init; }
    internal PaymentProviderPin Pin => new(ProviderId, AccountId, Environment, ConfigurationVersion);
}

internal sealed class PaymentProviderConfiguration(IConfiguration configuration, IHostEnvironment environment)
{
    internal const string LegacySimulatorProfileId = "legacy-simulator";
    internal const string LegacyRazorpayProfileId = "legacy-razorpay";

    internal PaymentProviderProfile[] Profiles => configuration.GetSection("Payments:Profiles").Get<PaymentProviderProfile[]>() ?? [];

    internal string? Failure(PaymentProviderProfile profile)
    {
        var profiles = Profiles;
        if (profiles.Length > 32 || !Identifier(profile.Id, 80) || !Identifier(profile.ProviderId, 40) ||
            !Identifier(profile.AccountId, 80) || !Identifier(profile.ConfigurationVersion, 80) ||
            profiles.Count(p => p.Id == profile.Id || p.Pin == profile.Pin) != 1 ||
            profile.Id is LegacySimulatorProfileId or LegacyRazorpayProfileId || profile.Pin == DevelopmentPaymentGateway.LocalPin ||
            profile.CredentialReference?.Length > 512 || profile.WebhookSecretReference?.Length > 512)
            return "InvalidProfile";
        if (profile.ProviderId == DevelopmentPaymentGateway.ProviderId)
        {
            if (!(environment.IsDevelopment() || environment.IsEnvironment("Testing")) || profile.Environment != "Development")
                return "SimulatorForbidden";
            return profile.WebhookSecretReference is { } reference && !WebhookSignature.Secret(configuration[reference])
                ? "CredentialUnavailable" : null;
        }
        if (profile.ProviderId != RazorpayConfiguration.ProviderId) return "UnknownProvider";
        if (profile.CredentialReference is not { Length: > 0 } section || !configuration.GetSection(section).Exists())
            return "CredentialUnavailable";
        return Razorpay(profile) is { } validated && validated.Pin == profile.Pin ? null : "InvalidProfile";
    }

    internal RazorpayConfiguration? Razorpay(PaymentProviderProfile profile) => profile.CredentialReference is { } reference
        ? RazorpayConfiguration.From(configuration.GetSection(reference).Get<RazorpayOptions>() ?? new()) : null;

    internal DevelopmentWebhookOptions Webhook(PaymentProviderProfile profile) =>
        new() { WebhookSecret = profile.WebhookSecretReference is { } reference ? configuration[reference] : null };

    private static bool Identifier(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '/' or '.');
}
