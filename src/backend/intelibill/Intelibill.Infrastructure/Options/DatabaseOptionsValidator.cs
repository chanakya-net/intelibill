using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Options;

/// <summary>
/// <c>Password</c> cannot carry a <c>[Required]</c> attribute any more, because
/// Entra authentication has no password. This keeps the password path from
/// silently starting without one and failing at the first connection instead.
/// </summary>
internal sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        List<string> errors = [];

        if (!options.UseEntraAuth && string.IsNullOrWhiteSpace(options.Password))
        {
            errors.Add("Configuration key 'Database:Password' is required unless 'Database:UseEntraAuth' is true.");
        }

        if (options.UseEntraAuth && !string.IsNullOrWhiteSpace(options.Password))
        {
            errors.Add("Configuration key 'Database:Password' must not be set when 'Database:UseEntraAuth' is true — it is ignored, and its presence usually means the wrong environment's configuration was applied.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
