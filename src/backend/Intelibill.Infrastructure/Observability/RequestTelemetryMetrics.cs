using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Intelibill.Infrastructure.Observability;

public sealed class RequestTelemetryMetrics
{
    public const string DatabaseDurationInstrumentName =
        "intelibill.http.server.database.duration";

    public const string DatabaseCommandCountInstrumentName =
        "intelibill.http.server.database.command.count";

    private readonly Histogram<double> _databaseDuration;
    private readonly Histogram<long> _databaseCommandCount;

    public RequestTelemetryMetrics(Meter meter)
    {
        _databaseDuration = meter.CreateHistogram<double>(
            DatabaseDurationInstrumentName,
            unit: "s",
            description: "Cumulative EF Core database command duration per inbound request.");
        _databaseCommandCount = meter.CreateHistogram<long>(
            DatabaseCommandCountInstrumentName,
            unit: "{command}",
            description: "Number of EF Core database commands per inbound request.");
    }

    public void RecordDatabaseTelemetry(
        RequestDatabaseTelemetry telemetry,
        string requestMethod,
        string route,
        int responseStatusCode)
    {
        TagList tags =
        [
            new("http.request.method", requestMethod),
            new("http.route", route),
            new("http.response.status_code", responseStatusCode),
        ];

        var durationSeconds = telemetry.CumulativeDuration.TotalSeconds;
        var commandCount = telemetry.CommandCount;

        _databaseDuration.Record(durationSeconds, tags);
        _databaseCommandCount.Record(commandCount, tags);

        Activity.Current?.SetTag(
            "intelibill.request.database.duration",
            durationSeconds);
        Activity.Current?.SetTag(
            "intelibill.request.database.command.count",
            commandCount);
    }
}
