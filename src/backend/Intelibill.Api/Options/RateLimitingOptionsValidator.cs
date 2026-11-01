using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Options;

/// <summary>
/// Guards the one setting here that weakens a protection rather than tuning it. Mirrors
/// <c>EmailOptionsValidator</c>: a development affordance is refused outright in any other
/// environment, so it cannot reach a deployed revision unnoticed.
/// </summary>
internal sealed class RateLimitingOptionsValidator(IHostEnvironment? environment = null) : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        if (!options.RelaxEndpointPolicies)
            return ValidateOptionsResult.Success;

        if (environment is not null && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            return ValidateOptionsResult.Fail(
                "RateLimiting:RelaxEndpointPolicies is allowed only in Development or Testing environments. " +
                "It replaces every endpoint's own rate limit with the global default, which would widen the " +
                "limits protecting the authentication endpoints.");

        return ValidateOptionsResult.Success;
    }
}
