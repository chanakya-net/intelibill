using Serilog.Events;

namespace Intelibill.Licensing.Api.Observability;

/// <summary>The licensing API's logging policy; the redaction itself is shared with the merchant API.</summary>
public sealed class LicensingLoggingOptions
{
    public const string SectionName = "Observability:Logging";

    public string MinimumLevel { get; init; } = nameof(LogEventLevel.Information);

    /// <summary>
    /// Masks email addresses, phone numbers and similar personal fields as well as credentials.
    /// On unless configured off: licensing records name commercial contacts and admin identities.
    /// </summary>
    public bool EnablePiiMasking { get; init; } = true;

    public int AsyncBufferSize { get; init; } = 10_000;

    internal LogEventLevel Level => Enum.Parse<LogEventLevel>(MinimumLevel, ignoreCase: true);

    internal bool IsValid() =>
        Enum.TryParse<LogEventLevel>(MinimumLevel, ignoreCase: true, out _) && AsyncBufferSize > 0;
}
