using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Options;

internal sealed class ExternalAuthOptionsValidator : IValidateOptions<ExternalAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, ExternalAuthOptions options)
    {
        List<string> errors = [];

        if (options.Google.Enabled)
        {
            EnsureRequired(options.Google.ClientId, "ExternalAuth:Google:ClientId", errors);
            EnsureRequired(options.Google.ClientSecret, "ExternalAuth:Google:ClientSecret", errors);
            EnsureRequired(options.Google.RedirectUri, "ExternalAuth:Google:RedirectUri", errors);
            EnsureRequired(options.Google.Scope, "ExternalAuth:Google:Scope", errors);
            EnsureRequired(options.Google.AuthorizationEndpoint, "ExternalAuth:Google:AuthorizationEndpoint", errors);
            EnsureRequired(options.Google.TokenEndpoint, "ExternalAuth:Google:TokenEndpoint", errors);
        }

        if (options.Facebook.Enabled)
        {
            EnsureRequired(options.Facebook.AppId, "ExternalAuth:Facebook:AppId", errors);
            EnsureRequired(options.Facebook.AppSecret, "ExternalAuth:Facebook:AppSecret", errors);
            EnsureRequired(options.Facebook.RedirectUri, "ExternalAuth:Facebook:RedirectUri", errors);
            EnsureRequired(options.Facebook.Scope, "ExternalAuth:Facebook:Scope", errors);
            EnsureRequired(options.Facebook.AuthorizationEndpoint, "ExternalAuth:Facebook:AuthorizationEndpoint", errors);
            EnsureRequired(options.Facebook.TokenEndpoint, "ExternalAuth:Facebook:TokenEndpoint", errors);
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static void EnsureRequired(string value, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"Configuration key '{key}' is required when provider is enabled.");
    }
}
