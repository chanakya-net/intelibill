using System.Diagnostics.Metrics;
using Intelibill.Api.Middleware;
using Intelibill.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Intelibill.Api.Unit.Tests.Middleware;

public sealed class RequestDatabaseTelemetryMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_RecordsDatabaseTelemetryWithLowCardinalityRequestTags()
    {
        using var meter = new Meter($"test.{Guid.NewGuid():N}");
        using var capture = new MetricCapture(meter.Name);
        var telemetryAccessor = new RequestDatabaseTelemetryAccessor();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/widgets/private-widget-id";

        RequestDelegate next = nextContext =>
        {
            telemetryAccessor.Current!.RecordCommand(TimeSpan.FromMilliseconds(25));
            nextContext.Response.StatusCode = StatusCodes.Status200OK;
            nextContext.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/widgets/{widgetId}"),
                order: 0,
                EndpointMetadataCollection.Empty,
                displayName: "widgets"));
            return Task.CompletedTask;
        };

        var metrics = new RequestTelemetryMetrics(meter);
        var middleware = new RequestDatabaseTelemetryMiddleware(
            next,
            metrics,
            telemetryAccessor);

        await middleware.InvokeAsync(context);

        var duration = Assert.Single(capture.DatabaseDurations);
        Assert.Equal(0.025, duration.Value, precision: 6);
        Assert.Equal("GET", duration.Tags["http.request.method"]);
        Assert.Equal("/api/widgets/{widgetId}", duration.Tags["http.route"]);
        Assert.Equal(200, duration.Tags["http.response.status_code"]);
        Assert.DoesNotContain(
            duration.Tags.Values,
            value => Equals(value, "/api/widgets/private-widget-id"));

        var count = Assert.Single(capture.CommandCounts);
        Assert.Equal(1, count.Value);
        Assert.Equal(duration.Tags, count.Tags);
    }

    [Fact]
    public async Task InvokeAsync_WhenPipelineThrows_RecordsZeroDatabaseTelemetryAndRethrows()
    {
        using var meter = new Meter($"test.{Guid.NewGuid():N}");
        using var capture = new MetricCapture(meter.Name);
        var telemetryAccessor = new RequestDatabaseTelemetryAccessor();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var expected = new InvalidOperationException("pipeline failed");
        RequestDelegate next = _ => throw expected;
        var metrics = new RequestTelemetryMetrics(meter);
        var middleware = new RequestDatabaseTelemetryMiddleware(
            next,
            metrics,
            telemetryAccessor);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context));

        Assert.Same(expected, actual);
        Assert.Equal(0, Assert.Single(capture.DatabaseDurations).Value);
        Assert.Equal(0, Assert.Single(capture.CommandCounts).Value);
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public MetricCapture(string meterName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>(
                (instrument, measurement, tags, _) =>
                {
                    if (instrument.Name == RequestTelemetryMetrics.DatabaseDurationInstrumentName)
                    {
                        DatabaseDurations.Add(new DoubleMeasurement(
                            measurement,
                            ToDictionary(tags)));
                    }
                });
            _listener.SetMeasurementEventCallback<long>(
                (instrument, measurement, tags, _) =>
                {
                    if (instrument.Name == RequestTelemetryMetrics.DatabaseCommandCountInstrumentName)
                    {
                        CommandCounts.Add(new LongMeasurement(
                            measurement,
                            ToDictionary(tags)));
                    }
                });
            _listener.Start();
        }

        public List<DoubleMeasurement> DatabaseDurations { get; } = [];
        public List<LongMeasurement> CommandCounts { get; } = [];

        public void Dispose() => _listener.Dispose();

        private static Dictionary<string, object?> ToDictionary(
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            return tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    private sealed record DoubleMeasurement(
        double Value,
        Dictionary<string, object?> Tags);

    private sealed record LongMeasurement(
        long Value,
        Dictionary<string, object?> Tags);
}
