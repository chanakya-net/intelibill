using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Options;

/// <summary>
/// Refuses a half-configured phone transport at startup rather than at the moment a user is
/// waiting for a code. Mirrors <see cref="EmailOptionsValidator"/>: nothing is required while the
/// feature is off.
/// </summary>
internal sealed class PhoneAuthOptionsValidator(IHostEnvironment? environment = null) : IValidateOptions<PhoneAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, PhoneAuthOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        List<string> errors = [];

        if (options.LocalCapture && environment is not null && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            errors.Add("PhoneAuth:LocalCapture is allowed only in Development or Testing environments.");

        if (!options.LocalCapture)
        {
            if (!string.Equals(options.Provider, "Twilio", StringComparison.OrdinalIgnoreCase))
                errors.Add("PhoneAuth:Provider must be 'Twilio'; no other transport is implemented.");

            EnsureRequired(options.AccountSid, "PhoneAuth:AccountSid", errors);
            EnsureRequired(options.AuthToken, "PhoneAuth:AuthToken", errors);

            if (string.IsNullOrWhiteSpace(options.FromNumber) && string.IsNullOrWhiteSpace(options.MessagingServiceSid))
                errors.Add("One of 'PhoneAuth:FromNumber' or 'PhoneAuth:MessagingServiceSid' is required when phone delivery is enabled.");

            // The domestic Indian gateway needs both ids on every submission; the international
            // gateway needs neither. One without the other is neither route.
            if (string.IsNullOrWhiteSpace(options.DltEntityId) != string.IsNullOrWhiteSpace(options.DltTemplateId))
                errors.Add("'PhoneAuth:DltEntityId' and 'PhoneAuth:DltTemplateId' must be set together, or both left empty to use the international gateway.");
        }

        // A shorter code is guessable within the attempt budget; a longer one is not the policy
        // the challenge expiry and attempt ceiling were chosen against.
        if (options.CodeLength is < 6 or > 10)
            errors.Add("'PhoneAuth:CodeLength' must be between 6 and 10.");

        if (options.CodeLifetimeMinutes is < 1 or > 30)
            errors.Add("'PhoneAuth:CodeLifetimeMinutes' must be between 1 and 30.");

        if (options.MaxAttempts is < 1 or > 10)
            errors.Add("'PhoneAuth:MaxAttempts' must be between 1 and 10.");

        if (options.ResendSpacingSeconds is < 15 or > 600)
            errors.Add("'PhoneAuth:ResendSpacingSeconds' must be between 15 and 600.");

        if (options.DestinationSendsPerHour is < 1 or > 50)
            errors.Add("'PhoneAuth:DestinationSendsPerHour' must be between 1 and 50.");

        if (!string.IsNullOrWhiteSpace(options.DefaultCountryCode) &&
            !System.Text.RegularExpressions.Regex.IsMatch(options.DefaultCountryCode.Trim(), @"^\+[1-9]\d{0,3}$"))
            errors.Add("'PhoneAuth:DefaultCountryCode' must be a country code such as '+91', or empty.");

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }

    private static void EnsureRequired(string value, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"Configuration key '{key}' is required when phone delivery is enabled.");
    }
}
