using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Intelibill.Api.Logging;

/// <summary>
/// What may be recorded about an exception: the types in its chain, where it was thrown from,
/// and the few provider fields a reviewed adapter has vouched for. Messages, <c>Data</c> and
/// provider payloads are never read, because any of them can carry a credential or a customer's
/// details.
/// </summary>
internal static partial class SafeExceptionDescription
{
    private const int MaxChainLength = 8;
    private const int MaxFrames = 48;

    public static IReadOnlyList<string> Types(Exception exception) =>
        [.. Chain(exception).Select(e => Name(e.GetType()))];

    /// <summary>
    /// <c>Type.Method</c> for each frame, outermost exception first, without file paths or line
    /// numbers. Code locations are not data, so they are safe to keep.
    /// </summary>
    public static IReadOnlyList<string> Frames(Exception exception)
    {
        var frames = new List<string>();
        var first = true;
        foreach (var link in Chain(exception))
        {
            if (!first)
                frames.Add($"--- inner {Name(link.GetType())} ---");
            first = false;

            foreach (var frame in new StackTrace(link, fNeedFileInfo: false).GetFrames())
            {
                if (frames.Count >= MaxFrames)
                    return frames;
                if (frame.GetMethod() is { } method)
                    frames.Add(Describe(method));
            }
        }

        return frames;
    }

    /// <summary>Adapter: a database's SQLSTATE, a five-character class code and nothing more.</summary>
    public static string? SqlState(Exception exception) =>
        Chain(exception)
            .OfType<DbException>()
            .Select(e => e.SqlState)
            .FirstOrDefault(state => state is not null && SqlStateFormat().IsMatch(state));

    /// <summary>Adapter: the HTTP status a downstream provider answered with.</summary>
    public static int? ProviderStatus(Exception exception) =>
        Chain(exception)
            .OfType<HttpRequestException>()
            .Select(e => (int?)e.StatusCode)
            .FirstOrDefault(status => status is not null);

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        var current = exception;
        for (var i = 0; current is not null && i < MaxChainLength; i++)
        {
            yield return current;
            current = current is AggregateException { InnerExceptions.Count: > 0 } aggregate
                ? aggregate.InnerExceptions[0]
                : current.InnerException;
        }
    }

    private static string Describe(MethodBase method)
    {
        var type = method.DeclaringType;
        var name = method.Name;

        // An async method runs as <Name>d__N.MoveNext; report the method the code calls it by.
        if (type is not null && name == "MoveNext" && type.Name.StartsWith('<'))
        {
            var end = type.Name.IndexOf('>', StringComparison.Ordinal);
            if (end > 1)
                name = type.Name[1..end];
            type = type.DeclaringType;
        }

        return type is null ? name : $"{Name(type)}.{name}";
    }

    // Namespace and nesting only: a generic's FullName drags in assembly-qualified arguments.
    private static string Name(Type type)
    {
        var nested = type.Name;
        for (var outer = type.DeclaringType; outer is not null; outer = outer.DeclaringType)
            nested = $"{outer.Name}+{nested}";
        return type.Namespace is null ? nested : $"{type.Namespace}.{nested}";
    }

    [GeneratedRegex("^[0-9A-Z]{5}$")]
    private static partial Regex SqlStateFormat();
}
