using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Intelibill.Infrastructure.Extensions;
using Intelibill.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed class RequestTelemetryIntegrationTests(PostgreSqlTestFixture fixture)
    : IAsyncLifetime, IDisposable
{
    private readonly ApiWebApplicationFactory _factory = new(fixture);

    public async Task InitializeAsync() => await _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task EveryInboundRequest_RecordsDatabaseDurationAndCommandCount()
    {
        using var capture = new MetricCapture(
            OpenTelemetryServiceCollectionExtensions.ApplicationMeterName);
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var pingResponse = await client.GetAsync("/api/ping");
        var unauthorizedResponse = await client.GetAsync("/api/items");
        var notFoundResponse = await client.GetAsync("/api/not-a-real-route/private-value");
        var registrationResponse = await client.PostAsJsonAsync(
            "/api/auth/register/email",
            new
            {
                email = $"telemetry-{Guid.NewGuid():N}@test.com",
                password = "Pass123!Aa",
                firstName = "Telemetry",
                lastName = "Test",
                phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
            });

        Assert.Equal(HttpStatusCode.OK, pingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, registrationResponse.StatusCode);

        AssertRequestTelemetry(
            capture,
            route: "api/ping",
            statusCode: StatusCodes.Status200OK,
            expectedCommandCount: 0);
        AssertRequestTelemetry(
            capture,
            route: "api/items",
            statusCode: StatusCodes.Status401Unauthorized,
            expectedCommandCount: 0);
        AssertRequestTelemetry(
            capture,
            route: "<unmatched>",
            statusCode: StatusCodes.Status404NotFound,
            expectedCommandCount: 0);

        var registration = capture.SingleFor(
            route: "api/auth/register/email",
            statusCode: StatusCodes.Status201Created);
        Assert.True(registration.DatabaseDurationSeconds >= 0);
        Assert.True(registration.CommandCount > 0);
    }

    private static void AssertRequestTelemetry(
        MetricCapture capture,
        string route,
        int statusCode,
        long expectedCommandCount)
    {
        var measurement = capture.SingleFor(route, statusCode);
        Assert.True(measurement.DatabaseDurationSeconds >= 0);
        Assert.Equal(expectedCommandCount, measurement.CommandCount);
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly ConcurrentQueue<Measurement> _measurements = new();
        private readonly ConcurrentDictionary<RequestKey, double> _durations = new();
        private readonly ConcurrentDictionary<RequestKey, long> _commandCounts = new();
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
                        var key = RequestKey.From(tags);
                        _durations[key] = measurement;
                        TryPair(key);
                    }
                });
            _listener.SetMeasurementEventCallback<long>(
                (instrument, measurement, tags, _) =>
                {
                    if (instrument.Name == RequestTelemetryMetrics.DatabaseCommandCountInstrumentName)
                    {
                        var key = RequestKey.From(tags);
                        _commandCounts[key] = measurement;
                        TryPair(key);
                    }
                });
            _listener.Start();
        }

        public Measurement SingleFor(string route, int statusCode)
        {
            var matches = _measurements
                .Where(measurement =>
                    measurement.Route == route &&
                    measurement.StatusCode == statusCode)
                .ToArray();

            return Assert.Single(matches);
        }

        public void Dispose() => _listener.Dispose();

        private void TryPair(RequestKey key)
        {
            if (_durations.TryGetValue(key, out var duration) &&
                _commandCounts.TryGetValue(key, out var count) &&
                _durations.TryRemove(key, out _) &&
                _commandCounts.TryRemove(key, out _))
            {
                _measurements.Enqueue(new Measurement(
                    key.Route,
                    key.StatusCode,
                    duration,
                    count));
            }
        }
    }

    private sealed record Measurement(
        string Route,
        int StatusCode,
        double DatabaseDurationSeconds,
        long CommandCount);

    private readonly record struct RequestKey(
        string Method,
        string Route,
        int StatusCode)
    {
        public static RequestKey From(
            ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var method = string.Empty;
            var route = string.Empty;
            var statusCode = 0;

            foreach (var tag in tags)
            {
                switch (tag.Key)
                {
                    case "http.request.method":
                        method = Assert.IsType<string>(tag.Value);
                        break;
                    case "http.route":
                        route = Assert.IsType<string>(tag.Value);
                        break;
                    case "http.response.status_code":
                        statusCode = Assert.IsType<int>(tag.Value);
                        break;
                }
            }

            return new RequestKey(method, route, statusCode);
        }
    }
}
