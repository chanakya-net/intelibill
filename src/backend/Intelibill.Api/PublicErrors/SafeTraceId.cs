using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Intelibill.Api.PublicErrors;

/// <summary>
/// The correlation id a failure is reported under, in the response and in its diagnostic.
/// </summary>
public static partial class SafeTraceId
{
    /// <summary>
    /// The request's W3C trace id when there is one: the runtime only accepts 32 hex characters
    /// from a <c>traceparent</c> header, and it is the id every log event of the request already
    /// carries. Otherwise the request's trace identifier, but only if it is short and plainly
    /// formed; anything else is replaced, so a caller can never put line breaks or forged
    /// records into a log line through it.
    /// </summary>
    public static string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity)
            return activity.TraceId.ToHexString();

        var identifier = context.TraceIdentifier;
        if (!string.IsNullOrEmpty(identifier) && WellFormed().IsMatch(identifier))
            return identifier;

        var generated = ActivityTraceId.CreateRandom().ToHexString();
        context.TraceIdentifier = generated;
        return generated;
    }

    [GeneratedRegex("^[A-Za-z0-9:._-]{1,64}$")]
    private static partial Regex WellFormed();
}
