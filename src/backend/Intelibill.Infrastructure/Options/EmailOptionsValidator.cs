using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace Intelibill.Infrastructure.Options;

/// <remarks>
/// Email enrollment, recovery and invitations are the only ways into an account, so a deployed
/// environment that cannot send mail has no working sign-up or recovery at all. Outside Development
/// and Testing, disabled mail, a non-HTTPS link origin or a missing outbox key therefore stops
/// startup instead of surfacing later as a 503 on every recovery request. Failure text names the
/// configuration key only, never its value.
/// </remarks>
internal sealed class EmailOptionsValidator(IHostEnvironment? environment = null, IConfiguration? configuration = null) : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var deployed = environment is not null && !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        if (!options.Enabled)
        {
            return deployed
                ? ValidateOptionsResult.Fail("Email:Enabled must be true outside Development and Testing: enrollment, recovery and invitations depend on mail delivery.")
                : ValidateOptionsResult.Success;
        }

        List<string> errors = [];
        if (options.LocalCapture && deployed)
            errors.Add("Email:LocalCapture is allowed only in Development or Testing environments.");

        EnsureRequired(options.Host, "Email:Host", errors);
        EnsurePortRange(options.Port, errors);
        if (!options.LocalCapture)
        {
            EnsureRequired(options.Username, "Email:Username", errors);
            EnsureRequired(options.Password, "Email:Password", errors);
        }
        else if (options.Host is not ("localhost" or "127.0.0.1" or "::1" or "mailpit"))
            errors.Add("Email:LocalCapture is restricted to localhost or the mailpit development container.");
        EnsureRequired(options.FromEmail, "Email:FromEmail", errors);
        EnsureRequired(options.FromName, "Email:FromName", errors);

        if (deployed && configuration is not null)
        {
            // Links in recovery mail are built from this origin; plain HTTP would carry the bearer
            // token in the clear, and a relative or malformed value would produce unusable mail.
            if (!Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out var baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps)
                errors.Add("Configuration key 'App:BaseUrl' must be an absolute https URL when email delivery is enabled.");
            if (!IsOutboxKey(configuration["Identity:OutboxKey"]))
                errors.Add("Configuration key 'Identity:OutboxKey' must contain a base64-encoded 32-byte key when email delivery is enabled.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }

    private static bool IsOutboxKey(string? value)
    {
        var buffer = new byte[32];
        return Convert.TryFromBase64String(value ?? string.Empty, buffer, out var written) && written == 32;
    }

    private static void EnsureRequired(string value, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"Configuration key '{key}' is required when email delivery is enabled.");
    }

    private static void EnsurePortRange(int port, List<string> errors)
    {
        if (port is < 1 or > 65535)
            errors.Add("Configuration key 'Email:Port' must be between 1 and 65535.");
    }
}
