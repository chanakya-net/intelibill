using System.Globalization;
using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Parsing;

namespace Intelibill.Shared.Web.Logging;

/// <summary>
/// Rewrites a log event into one that is safe to keep: the exception object is replaced by its
/// safe description, credentials (and, when asked, personal data) are masked by property name
/// at every depth, and recognisable secrets are scrubbed out of string values and message text.
/// Everything is bounded, so a hostile or accidental value cannot make a log event unbounded.
/// </summary>
/// <remarks>
/// This is a backstop, not the boundary: a secret written into free prose cannot be found
/// reliably, so sources must still not log one. It catches the structured and well-formed cases
/// — a DTO, a header dictionary, a URL with a token — that reach a sink from code we do not own.
/// </remarks>
public sealed partial class LogEventRedactor(bool maskPersonalData)
{
    public const string Masked = "***MASKED***";
    public const string Truncated = "***TRUNCATED***";
    public const int MaxDepth = 8;
    public const int MaxElements = 100;
    public const int MaxStringLength = 4096;
    private const string DetailsRedacted = "(details redacted)";

    // Normalized names (letters and digits, lower case) whose value is a credential wherever it
    // appears. "code" is exact only: StatusCode and ErrorCode are not secrets.
    private static readonly HashSet<string> CredentialNames = new(StringComparer.Ordinal)
    {
        "authorization", "proxyauthorization", "cookie", "setcookie", "querystring",
        "code", "verificationcode", "confirmationcode", "otp", "otpcode", "pin",
        "token", "accesstoken", "refreshtoken", "idtoken", "rawtoken", "bearer",
        "password", "secret", "clientsecret", "apikey", "xapikey", "privatekey",
        "signature", "connectionstring", "credential", "credentials", "link", "resetlink",
    };

    private static readonly string[] CredentialSuffixes =
    [
        "password", "token", "secret", "apikey", "privatekey", "connectionstring", "cookie", "link", "otp", "credential", "credentials",
    ];

    private static readonly HashSet<string> PersonalNames = new(StringComparer.Ordinal)
    {
        "email", "phone", "phonenumber", "mobile", "cardnumber", "aadhaar", "pan",
    };

    private static readonly string[] PersonalSuffixes = ["email", "emailaddress", "phone", "phonenumber"];

    private readonly bool _maskPersonalData = maskPersonalData;

    public LogEvent Redact(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var properties = new List<LogEventProperty>(logEvent.Properties.Count + 4);
        foreach (var (name, value) in logEvent.Properties)
            properties.Add(new LogEventProperty(name, IsSensitive(name) ? new ScalarValue(Masked) : Redact(value, 0)));

        if (logEvent.Exception is { } exception)
            AddExceptionDescription(properties, logEvent.Properties, exception);

        return new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            exception: null,
            RedactTemplate(logEvent.MessageTemplate),
            properties,
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);
    }

    private LogEventPropertyValue Redact(LogEventPropertyValue value, int depth)
    {
        if (depth >= MaxDepth)
            return new ScalarValue(Truncated);

        switch (value)
        {
            case ScalarValue scalar:
                return RedactScalar(scalar);

            // A destructured exception ({@Failure}) carries Message, Data and StackTrace as properties.
            case StructureValue { TypeTag: { } tag } when tag.EndsWith("Exception", StringComparison.Ordinal):
                return new ScalarValue($"{tag} {DetailsRedacted}");

            // A header list is a sequence of pairs; the secret is in Value, the name in Key.
            case StructureValue { TypeTag: { } pairTag } pair
                when pairTag.StartsWith("KeyValuePair", StringComparison.Ordinal)
                    && pair.Properties.FirstOrDefault(p => p.Name == "Key")?.Value is ScalarValue { Value: string key }
                    && IsSensitive(key):
                return new StructureValue(
                    [.. pair.Properties.Select(p => p.Name == "Value" ? new LogEventProperty(p.Name, new ScalarValue(Masked)) : p)],
                    pairTag);

            case StructureValue structure:
                return new StructureValue(
                    [.. structure.Properties
                        .Take(MaxElements)
                        .Select(p => new LogEventProperty(p.Name, IsSensitive(p.Name) ? new ScalarValue(Masked) : Redact(p.Value, depth + 1)))],
                    structure.TypeTag);

            case SequenceValue sequence:
                var elements = sequence.Elements.Take(MaxElements).Select(e => Redact(e, depth + 1)).ToList();
                if (sequence.Elements.Count > MaxElements)
                    elements.Add(new ScalarValue(Truncated));
                return new SequenceValue(elements);

            case DictionaryValue dictionary:
                // Masking can turn distinct keys into the same one (two emails); a dictionary
                // cannot hold both, and throwing here would lose the whole event.
                return new DictionaryValue(dictionary.Elements
                    .Take(MaxElements)
                    .Select(pair => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        RedactScalar(pair.Key),
                        pair.Key.Value is string key && IsSensitive(key)
                            ? new ScalarValue(Masked)
                            : Redact(pair.Value, depth + 1)))
                    .DistinctBy(pair => pair.Key));

            default:
                return value;
        }
    }

    private ScalarValue RedactScalar(ScalarValue scalar) => scalar.Value switch
    {
        null => scalar,
        string text => Scrubbed(scalar, text),
        // Rendered through ToString() otherwise, which is the exception's message.
        Exception exception => new ScalarValue(string.Join(" <- ", SafeExceptionDescription.Types(exception))),
        Uri uri => Scrubbed(scalar, uri.OriginalString),
        bool or char or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or Guid or DateTime or DateTimeOffset or DateOnly
            or TimeOnly or TimeSpan or Enum => scalar,
        // Any other object would be rendered by its ToString(), and a record's ToString() lists
        // every property — a request DTO's password included.
        var other => Scrubbed(scalar, Convert.ToString(other, CultureInfo.InvariantCulture) ?? string.Empty),
    };

    private ScalarValue Scrubbed(ScalarValue original, string text)
    {
        // Serilog captures an exception passed as an ordinary value as its ToString() text,
        // which is the message, the inner exceptions and the stack. Keep only its type.
        if (ExceptionText().Match(text) is { Success: true } exceptionText)
            return new ScalarValue($"{exceptionText.Groups["type"].Value} {DetailsRedacted}");

        // A KeyValuePair captured without destructuring renders as "[Key, Value]".
        if (PairText().Match(text) is { Success: true } pairText && IsSensitive(pairText.Groups["key"].Value))
            return new ScalarValue($"[{pairText.Groups["key"].Value}, {Masked}]");

        var scrubbed = Scrub(text);
        return original.Value is string && ReferenceEquals(scrubbed, text) ? original : new ScalarValue(scrubbed);
    }

    private string Scrub(string text)
    {
        var result = text.Length > MaxStringLength ? string.Concat(text.AsSpan(0, MaxStringLength), Truncated) : text;

        if (result.AsSpan().IndexOfAny('=', ':') >= 0)
        {
            result = NamedSecret().Replace(result, match => $"{match.Groups["name"].Value}{match.Groups["sep"].Value}{match.Groups["quote"].Value}{Masked}");
            result = QueryCode().Replace(result, match => string.Concat(match.ValueSpan[..5], Masked));
        }

        if (result.Contains("Bearer", StringComparison.OrdinalIgnoreCase) || result.Contains("Basic", StringComparison.OrdinalIgnoreCase))
            result = AuthorizationScheme().Replace(result, match => $"{match.Groups["scheme"].Value} {Masked}");
        if (_maskPersonalData && result.Contains('@', StringComparison.Ordinal))
            result = EmailAddress().Replace(result, Masked);

        return result == text ? text : result;
    }

    private MessageTemplate RedactTemplate(MessageTemplate template)
    {
        // Text passed where a template belongs ($"..." or ex.ToString() into a logger) is the
        // template itself.
        if (ExceptionText().Match(template.Text) is { Success: true } exceptionText)
            return new MessageTemplateParser().Parse($"{exceptionText.Groups["type"].Value} {DetailsRedacted}");

        var text = Scrub(template.Text);
        return ReferenceEquals(text, template.Text) ? template : new MessageTemplateParser().Parse(text);
    }

    private static void AddExceptionDescription(
        List<LogEventProperty> properties,
        IReadOnlyDictionary<string, LogEventPropertyValue> existing,
        Exception exception)
    {
        void Add(string name, LogEventPropertyValue value)
        {
            if (!existing.ContainsKey(name))
                properties.Add(new LogEventProperty(name, value));
        }

        Add("ExceptionTypes", new SequenceValue(SafeExceptionDescription.Types(exception).Select(t => new ScalarValue(t))));
        Add("ExceptionFrames", new SequenceValue(SafeExceptionDescription.Frames(exception).Select(f => new ScalarValue(f))));
        if (SafeExceptionDescription.SqlState(exception) is { } sqlState)
            Add("SqlState", new ScalarValue(sqlState));
        if (SafeExceptionDescription.ProviderStatus(exception) is { } providerStatus)
            Add("ProviderStatus", new ScalarValue(providerStatus));
    }

    private bool IsSensitive(string name)
    {
        var normalized = Normalize(name);
        if (CredentialNames.Contains(normalized) || CredentialSuffixes.Any(s => normalized.EndsWith(s, StringComparison.Ordinal)))
            return true;
        return _maskPersonalData
            && (PersonalNames.Contains(normalized) || PersonalSuffixes.Any(s => normalized.EndsWith(s, StringComparison.Ordinal)));
    }

    private static string Normalize(string name)
    {
        Span<char> buffer = name.Length <= 128 ? stackalloc char[name.Length] : new char[name.Length];
        var length = 0;
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
                buffer[length++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..length]);
    }

    // name=value, name: value, "name":"value" and name='value' for credential-shaped names, as
    // they appear in query strings, fragments, connection strings, headers, JSON (this app's own
    // camelCase included) and record ToString() output. A quoted value runs to its closing quote.
    [GeneratedRegex(
        """(?<![A-Za-z0-9_])(?<name>[A-Za-z0-9_-]*?(?:token|password|passwd|secret|api[_-]?key)|pwd|otp|signature|sig|(?:verification|confirmation|otp|reset|sms)[_-]?code)(?<sep>["']?\s*[=:]\s*)(?:(?<quote>["'])(?!\*\*\*)[^"']*|(?!\*\*\*)[^&\s#;,'"{}\]]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecret();

    // A bare "code" is a secret only as a query or fragment parameter; "status code: 503" is not.
    [GeneratedRegex(@"(?<=[?&#;])code=(?!\*\*\*)[^&\s#'""]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QueryCode();

    // An exception's ToString(): its type, an HRESULT for external exceptions such as
    // DbException ("Npgsql.PostgresException (0x80004005): ..."), then the message.
    [GeneratedRegex(@"^(?<type>[A-Za-z_][\w.+`]*Exception)(?: \((?:0x[0-9A-Fa-f]+|-?\d+)\))?(?::|\r?\n)", RegexOptions.CultureInvariant)]
    private static partial Regex ExceptionText();

    [GeneratedRegex(@"^\[(?<key>[^,\[\]]{1,128}), .*\]$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PairText();

    // Credential-length tokens only, so "Bearer authentication" or "Basic Rice 5kg" survive.
    [GeneratedRegex(
        @"\b(?<scheme>Bearer)\s+(?!\*\*\*)[A-Za-z0-9\-._~+/]{16,}=*|\b(?<scheme>Basic)\s+(?!\*\*\*)[A-Za-z0-9+/]{12,}={0,2}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationScheme();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddress();
}
