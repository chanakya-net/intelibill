using Serilog.Core;
using Serilog.Events;

namespace Intelibill.Api.Logging;

/// <summary>
/// Replaces known PII property names in log events with "***MASKED***".
/// Handles structured log properties; message template tokens are scrubbed
/// by key name match so original template shape is preserved.
/// </summary>
public sealed class PiiScrubbingEnricher : ILogEventEnricher
{
    private const string MaskedValue = "***MASKED***";

    private static readonly HashSet<string> PiiFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "email",
        "phone",
        "password",
        "cardNumber",
        "aadhaar",
        "pan",
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var key in PiiFieldNames)
        {
            if (logEvent.Properties.ContainsKey(key))
            {
                logEvent.AddOrUpdateProperty(
                    propertyFactory.CreateProperty(key, MaskedValue));
            }
        }
    }
}
