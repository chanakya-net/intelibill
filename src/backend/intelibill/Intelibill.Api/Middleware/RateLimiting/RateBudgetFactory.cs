using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Intelibill.Api.Options;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Middleware.RateLimiting;

/// <summary>
/// The budgets one request is counted against. <see cref="Request"/> comes first and is the only
/// one whose numbers reach the caller in headers.
/// </summary>
public sealed record RateBudgetSet(IReadOnlyList<RateBudget> Request, IReadOnlyList<RateBudget> Subject)
{
    public IReadOnlyList<RateBudget> All => [.. Request, .. Subject];
}

/// <summary>
/// Composes a request's budgets from things the caller cannot vary at will.
/// <list type="bullet">
/// <item>The endpoint is its method and route template, never the raw path, so
/// <c>/api/items/1</c> and <c>/api/items/2</c> share one allowance.</item>
/// <item>The client address is the one forwarded-header processing settled on, which only a
/// trusted proxy can change. IPv6 is counted per /64, the smallest block one subscriber is usually
/// given, so cycling through a single allocation does not mint fresh buckets.</item>
/// <item>An account or destination is normalized the way the identity services normalize it,
/// so case, spacing and phone formatting all land in one bucket.</item>
/// </list>
/// Material is hashed through <see cref="RateLimitKeyHasher"/> before it leaves this class.
/// </summary>
public sealed class RateBudgetFactory(
    IOptions<RateLimitingOptions> options,
    RateLimitKeyHasher hasher,
    IPhoneAuthPolicy phonePolicy)
{
    internal const string AccountPolicyId = "subj:account";
    internal const string DestinationPolicyId = "subj:destination";
    internal const string DestinationDailyPolicyId = "subj:destination:day";

    private const int MaxPolicyIdLength = 200;
    private const int SecondsPerDay = 86_400;

    public RateBudgetSet Create(ActionExecutingContext context, RateLimitPolicy policy)
    {
        var http = context.HttpContext;
        var request = new List<RateBudget>();
        Add(
            request,
            RoutePolicyId(context),
            $"{UserMaterial(http.User) ?? "anonymous"}|{NormalizeAddress(http.Connection.RemoteIpAddress)}",
            policy.Limit,
            policy.PeriodInMinutes * 60,
            policy.BackoffMinutes * 60);

        var subject = new List<RateBudget>();
        if (policy.Subject != RateLimitSubject.None && SubjectMaterial(context, policy.Subject) is { } material)
        {
            var value = options.Value;
            var (budget, policyId) = policy.Subject == RateLimitSubject.Account
                ? (value.Account, AccountPolicyId)
                : (value.Destination, DestinationPolicyId);

            if (value.RelaxEndpointPolicies)
            {
                Add(subject, policyId, material, value.Limit, value.PeriodInMinutes * 60, 0);
            }
            else
            {
                Add(subject, policyId, material, budget.Limit, budget.PeriodInMinutes * 60, 0);
                if (budget.DailyLimit > 0)
                    Add(subject, DestinationDailyPolicyId, material, budget.DailyLimit, SecondsPerDay, 0);
            }
        }

        return new RateBudgetSet(request, subject);
    }

    private void Add(List<RateBudget> budgets, string policyId, string material, int limit, int windowSeconds, int backoffSeconds)
    {
        foreach (var key in hasher.Hash($"{policyId}\n{material}"))
            budgets.Add(new RateBudget(policyId, key, limit, windowSeconds, backoffSeconds));
    }

    private static string RoutePolicyId(ActionExecutingContext context)
    {
        var method = context.HttpContext.Request.Method.ToUpperInvariant();
        var template = context.ActionDescriptor.AttributeRouteInfo?.Template;
        if (string.IsNullOrWhiteSpace(template))
        {
            var values = context.ActionDescriptor.RouteValues;
            template = values.TryGetValue("controller", out var controller) && values.TryGetValue("action", out var action)
                ? $"{controller}/{action}"
                : "unrouted";
        }

        var policyId = $"req:{method} {template.Trim('/').ToLowerInvariant()}";
        return policyId.Length <= MaxPolicyIdLength
            ? policyId
            : $"req:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(policyId)))}";
    }

    private static string? UserMaterial(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(id) ? null : $"user:{id}";
    }

    internal static string NormalizeAddress(IPAddress? address)
    {
        if (address is null)
            return "unknown";

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return string.Create(CultureInfo.InvariantCulture, $"{new IPAddress(bytes)}/64");
    }

    private string? SubjectMaterial(ActionExecutingContext context, RateLimitSubject kind)
    {
        // Only a bound model can name a subject: anything the caller left out of the body simply
        // has no subject budget, and the request budget above still applies to it.
        var source = context.ActionArguments.Values.OfType<IRateLimitSubject>().FirstOrDefault();
        if (source is not null)
            return NormalizeSubject(source.Subject);

        // An account action with no named account is one on the signed-in account itself.
        return kind == RateLimitSubject.Account ? UserMaterial(context.HttpContext.User) : null;
    }

    private string? NormalizeSubject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Contains('@', StringComparison.Ordinal))
            return $"email:{trimmed.ToLowerInvariant()}";

        if (PhoneNumberFormat.TryNormalize(trimmed, phonePolicy.DefaultCountryCode, out var e164))
            return $"phone:{e164}";

        return $"raw:{trimmed.ToLowerInvariant()}";
    }
}
