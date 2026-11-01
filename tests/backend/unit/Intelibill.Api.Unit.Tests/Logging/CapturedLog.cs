using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace Intelibill.Api.Unit.Tests.Logging;

/// <summary>
/// A Serilog pipeline that ends in memory, so a test can read what a sink would actually
/// have received and how the console formatter would have rendered it.
/// </summary>
internal sealed class CapturedLog : IDisposable
{
    private readonly CaptureSink _sink = new();
    private readonly Logger _logger;
    private readonly SerilogLoggerFactory _factory;

    public CapturedLog()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(_sink)
            .CreateLogger();
        _factory = new SerilogLoggerFactory(_logger);
    }

    public IReadOnlyList<LogEvent> Events => [.. _sink.Events];

    public ILogger<T> CreateLogger<T>() => _factory.CreateLogger<T>();

    /// <summary>Every event as the console sink writes it (compact JSON, one line each).</summary>
    public string RenderedCompact() => Render(new CompactJsonFormatter());

    /// <summary>Every event with its message rendered, as an exporter's body would carry it.</summary>
    public string RenderedMessages() => Render(new RenderedCompactJsonFormatter());

    private string Render(ITextFormatter formatter)
    {
        using var writer = new StringWriter();
        foreach (var logEvent in _sink.Events)
            formatter.Format(logEvent, writer);
        return writer.ToString();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _logger.Dispose();
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
