using Intelibill.Api.Middleware.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Options;

/// <summary>
/// Guards the settings here that weaken a protection rather than tune it. Mirrors
/// <c>EmailOptionsValidator</c>: a development affordance is refused outright in any other
/// environment, so it cannot reach a deployed revision unnoticed.
/// </summary>
internal sealed class RateLimitingOptionsValidator(IHostEnvironment? environment = null) : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        List<string> errors = [];
        var local = environment is null || environment.IsDevelopment() || environment.IsEnvironment("Testing");

        if (options.RelaxEndpointPolicies && !local)
            errors.Add(
                "RateLimiting:RelaxEndpointPolicies is allowed only in Development or Testing environments. " +
                "It replaces every endpoint's own rate limit with the global default, which would widen the " +
                "limits protecting the authentication endpoints.");

        // Without a shared key each replica would hash the same caller into a different bucket,
        // multiplying every allowance by the replica count.
        if (string.IsNullOrWhiteSpace(options.BucketKey))
        {
            if (!local)
                errors.Add("RateLimiting:BucketKey is required outside Development and Testing: every replica must hash buckets with the same key.");
        }
        else if (!RateLimitKeyHasher.TryParseKey(options.BucketKey, out _))
        {
            errors.Add("RateLimiting:BucketKey must be a base64-encoded 32-byte key.");
        }

        if (!string.IsNullOrWhiteSpace(options.PreviousBucketKey) && !RateLimitKeyHasher.TryParseKey(options.PreviousBucketKey, out _))
            errors.Add("RateLimiting:PreviousBucketKey must be a base64-encoded 32-byte key.");

        ValidateSubject(options.Account, "Account", errors);
        ValidateSubject(options.Destination, "Destination", errors);

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }

    private static void ValidateSubject(SubjectBudgetOptions budget, string section, List<string> errors)
    {
        if (budget.Limit < 1)
            errors.Add($"RateLimiting:{section}:Limit must be at least 1.");
        if (budget.PeriodInMinutes < 1)
            errors.Add($"RateLimiting:{section}:PeriodInMinutes must be at least 1.");
        if (budget.DailyLimit < 0)
            errors.Add($"RateLimiting:{section}:DailyLimit must be zero or more.");
    }
}
