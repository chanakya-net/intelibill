using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Intelibill.P0102ConsumerRegressionTests;

/// <summary>Synthetic, non-production values that must never leave either host.</summary>
internal static class Sentinels
{
    internal const string Bearer = "eyJp0102-bearer-sentinel";
    internal const string Password = "p0102-db-password-sentinel";
    internal const string Email = "p0102.customer@example.test";
    internal const string Data = "p0102-data-sentinel";
    internal const string NestedToken = "p0102-nested-token-sentinel";
    internal const string NestedHeader = "p0102-nested-header-sentinel";
    internal const string NestedRefresh = "p0102-nested-refresh-sentinel";

    internal static readonly string[] Credentials = [Bearer, Password, Data, NestedToken, NestedHeader, NestedRefresh];

    internal static InvalidOperationException SecretBearingException()
    {
        var exception = new InvalidOperationException(
            $"Authorization: Bearer {Bearer}; Host=db;Password={Password}; customer {Email}",
            new TimeoutException($"inner for {Email}"));
        exception.Data["credential"] = Data;
        return exception;
    }

    internal static void AssertNone(string text, bool includingEmail = true)
    {
        foreach (var sentinel in Credentials)
            Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
        if (includingEmail)
            Assert.DoesNotContain(Email, text, StringComparison.Ordinal);
    }
}

/// <summary>
/// Appended after the host's own pipeline, so an unmatched path reaches it and its failure
/// travels back through whatever error handling the host installed. Before throwing it marks
/// the request span the way EF Core and Wolverine instrumentation do: error status described by
/// the exception message, plus exception message and stack tags.
/// </summary>
internal sealed class FailureProbe : IStartupFilter
{
    internal const string Path = "/p0102/throw";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use((HttpContext context, RequestDelegate _) =>
        {
            if (!context.Request.Path.Equals(Path, StringComparison.Ordinal))
                return Task.CompletedTask;

            var exception = Sentinels.SecretBearingException();
            Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
            Activity.Current?.SetTag("exception.type", exception.GetType().FullName);
            Activity.Current?.SetTag("exception.message", exception.Message);
            Activity.Current?.SetTag("exception.stacktrace", exception.ToString());
            throw exception;
        });
    };
}

/// <summary>A test-only background worker whose failure is logged with the exception object.</summary>
internal sealed partial class WorkerProbe(ILogger<WorkerProbe> logger) : IHostedService
{
    internal const string Marker = "P0102 worker failure";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogWorkerFailure(logger, Sentinels.SecretBearingException(), Sentinels.NestedToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = Marker + " settling {Token}")]
    private static partial void LogWorkerFailure(ILogger logger, Exception exception, string token);
}

/// <summary>Receives events exactly as the production sinks do: after redaction.</summary>
internal sealed class CapturingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    public async Task<LogEvent> WaitForAsync(Func<LogEvent, bool> predicate, string description)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (_events.FirstOrDefault(predicate) is { } found)
                return found;
            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException($"No log event reached the sink: {description}.");
    }

    public string Rendered()
    {
        using var writer = new StringWriter();
        var compact = new CompactJsonFormatter();
        var rendered = new RenderedCompactJsonFormatter();
        foreach (var logEvent in _events)
        {
            compact.Format(logEvent, writer);
            rendered.Format(logEvent, writer);
        }

        return writer.ToString();
    }

    public static string? Scalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;
}

internal sealed record CapturedSpan(
    string TraceId,
    ActivityKind Kind,
    ActivityStatusCode Status,
    string? StatusDescription,
    IReadOnlyDictionary<string, object?> Tags);

/// <summary>Registered after the host's processors; sees each span as an exporter would.</summary>
internal sealed class CapturingSpanProcessor : BaseProcessor<Activity>
{
    private readonly ConcurrentQueue<CapturedSpan> _spans = new();

    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _spans.Enqueue(new CapturedSpan(
            data.TraceId.ToHexString(),
            data.Kind,
            data.Status,
            data.StatusDescription,
            data.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));
    }

    public async Task<CapturedSpan> WaitForServerSpanAsync(string traceId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (_spans.FirstOrDefault(span => span.TraceId == traceId && span.Kind == ActivityKind.Server) is { } found)
                return found;
            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException($"No server span for trace {traceId} reached the exporter position.");
    }
}
