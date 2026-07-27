using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Options;

/// <summary>
/// The two signing modes need different configuration, and the dangerous mistake
/// is a deployed environment quietly falling back to HMAC because its Key Vault
/// settings did not arrive. That produces a working application whose tokens can
/// be forged by anyone holding the secret.
/// </summary>
internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    // HS256 keys shorter than the 256-bit hash weaken the MAC, and
    // Microsoft.IdentityModel rejects them outright.
    private const int MinimumHmacSecretLength = 32;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        List<string> errors = [];

        switch (options.SigningMode)
        {
            case JwtSigningMode.Hmac:
                if (string.IsNullOrWhiteSpace(options.Secret))
                {
                    errors.Add("Configuration key 'Jwt:Secret' is required when 'Jwt:SigningMode' is Hmac.");
                }
                else if (options.Secret.Length < MinimumHmacSecretLength)
                {
                    errors.Add($"Configuration key 'Jwt:Secret' must be at least {MinimumHmacSecretLength} characters.");
                }

                break;

            case JwtSigningMode.KeyVault:
                if (string.IsNullOrWhiteSpace(options.KeyVaultKeyId))
                {
                    errors.Add("Configuration key 'Jwt:KeyVaultKeyId' is required when 'Jwt:SigningMode' is KeyVault.");
                }
                else if (!Uri.TryCreate(options.KeyVaultKeyId, UriKind.Absolute, out _))
                {
                    errors.Add("Configuration key 'Jwt:KeyVaultKeyId' must be an absolute key identifier URI.");
                }

                if (!string.IsNullOrWhiteSpace(options.Secret))
                {
                    errors.Add("Configuration key 'Jwt:Secret' must not be set when 'Jwt:SigningMode' is KeyVault — it is ignored, and its presence usually means another environment's configuration was applied.");
                }

                break;

            default:
                errors.Add($"Configuration key 'Jwt:SigningMode' has unsupported value '{options.SigningMode}'.");
                break;
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
