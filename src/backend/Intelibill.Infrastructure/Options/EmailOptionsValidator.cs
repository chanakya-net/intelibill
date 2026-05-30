using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Options;

internal sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        List<string> errors = [];

        EnsureRequired(options.Host, "Email:Host", errors);
        EnsurePortRange(options.Port, errors);
        EnsureRequired(options.Username, "Email:Username", errors);
        EnsureRequired(options.Password, "Email:Password", errors);
        EnsureRequired(options.FromEmail, "Email:FromEmail", errors);
        EnsureRequired(options.FromName, "Email:FromName", errors);

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
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
