using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Intelibill.Integration.Tests;

/// <summary>
/// Plants credentials, a database password and a customer's email in a real failure inside the
/// real host, then reads everything that left it — the HTTP body, the NDJSON stream and every
/// log event as a sink received it — and checks none of them escaped, while the trace id in the
/// response still finds the failure's diagnostic.
/// </summary>
[Collection("Integration Tests")]
public sealed class ErrorDisclosureIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string BearerSentinel = "eyJdisclosure-bearer-sentinel";
    private const string PasswordSentinel = "disclosure-db-password-sentinel";
    private const string EmailSentinel = "disclosure.customer@example.test";
    private const string DataSentinel = "disclosure-data-sentinel";

    private readonly CapturingSink _sink = new();
    private readonly FailingSnapshotService _snapshots = new();
    private readonly ApiWebApplicationFactory _factory;

    public ErrorDisclosureIntegrationTests(PostgreSqlTestFixture fixture)
    {
        _factory = new ApiWebApplicationFactory(fixture, services =>
        {
            services.AddSingleton<ILogEventSink>(_sink);
            services.RemoveAll<IOfflineSalesSnapshotStreamingService>();
            services.AddSingleton<IOfflineSalesSnapshotStreamingService>(_snapshots);
        });
    }

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
    public async Task UnexpectedFailure_ReturnsSafeProblemFoundByItsTraceId()
    {
        using var client = CreateClient();
        var token = await SignInWithShopAsync(client);
        _snapshots.FailBeforeFirstRecord = true;

        using var response = await SendSnapshotRequestAsync(client, token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Internal.Unexpected", json.RootElement.GetProperty("code").GetString());
        var traceId = json.RootElement.GetProperty("traceId").GetString()!;
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
        AssertNoSentinel(body);

        var diagnostic = await _sink.WaitForAsync(e =>
            Scalar(e, "TraceId") == traceId && Scalar(e, "ErrorCode") == "Internal.Unexpected");
        Assert.Equal(LogEventLevel.Error, diagnostic.Level);
        // The request log carried the exception object; it arrives as types and frames only.
        await _sink.WaitForAsync(e => e.Properties.ContainsKey("ExceptionTypes") && !e.Properties.ContainsKey("ErrorCode"));
        AssertNoSentinel(_sink.Rendered());
    }

    [Fact]
    public async Task StreamFailureAfterFirstRecord_EndsWithSafeErrorAndNoComplete()
    {
        using var client = CreateClient();
        var token = await SignInWithShopAsync(client);
        _snapshots.FailBeforeFirstRecord = false;

        using var response = await SendSnapshotRequestAsync(client, token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var records = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToArray();
        Assert.Collection(
            records,
            r => Assert.Equal("metadata", r.GetProperty("type").GetString()),
            r => Assert.Equal("error", r.GetProperty("type").GetString()));
        var error = records[1].GetProperty("error");
        Assert.Equal("OfflineSnapshot.StreamFailed", error.GetProperty("code").GetString());
        var traceId = error.GetProperty("traceId").GetString()!;
        AssertNoSentinel(body);

        await _sink.WaitForAsync(e =>
            Scalar(e, "TraceId") == traceId && Scalar(e, "ErrorCode") == "OfflineSnapshot.StreamFailed");
        AssertNoSentinel(_sink.Rendered());
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static async Task<HttpResponseMessage> SendSnapshotRequestAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sales/offline-snapshot/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<string> SignInWithShopAsync(HttpClient client)
    {
        var enrolled = await client.EnrollAsync(new
        {
            email = $"disclosure-{Guid.NewGuid():N}@test.com",
            password = "Pass123!Aa",
            firstName = "Disclosure",
            lastName = "Probe",
            phoneNumber = $"+91{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
        });
        enrolled.EnsureSuccessStatusCode();
        var accessToken = (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            name = $"Shop {Guid.NewGuid():N}",
            address = "42 MG Road",
            city = "Bengaluru",
            state = "Karnataka",
            pincode = "560001",
        });
        using var created = await client.SendAsync(request);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private static InvalidOperationException SecretBearingException()
    {
        var exception = new InvalidOperationException(
            $"Authorization: Bearer {BearerSentinel}; Host=db;Password={PasswordSentinel}; customer {EmailSentinel}",
            new TimeoutException($"inner for {EmailSentinel}"));
        exception.Data["credential"] = DataSentinel;
        return exception;
    }

    private static void AssertNoSentinel(string text)
    {
        foreach (var sentinel in new[] { BearerSentinel, PasswordSentinel, EmailSentinel, DataSentinel })
            Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
    }

    private static string? Scalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private sealed class FailingSnapshotService : IOfflineSalesSnapshotStreamingService
    {
        public bool FailBeforeFirstRecord { get; set; }

        public Task<ErrorOr<Success>> ValidateAccessAsync(Guid userId, Guid activeShopId, CancellationToken cancellationToken) =>
            Task.FromResult<ErrorOr<Success>>(Result.Success);

        public async IAsyncEnumerable<IOfflineSalesSnapshotStreamRecord> StreamAsync(
            Guid activeShopId,
            Guid snapshotId,
            DateTimeOffset startedAt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!FailBeforeFirstRecord)
            {
                yield return new OfflineSalesSnapshotMetadataRecord(
                    new OfflineSalesSnapshotMetadata(snapshotId, activeShopId, 1, startedAt));
            }

            await Task.Yield();
            throw SecretBearingException();
        }
    }

    /// <summary>Receives events exactly as the production sinks do: after redaction.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

        public async Task<LogEvent> WaitForAsync(Func<LogEvent, bool> predicate)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (_events.FirstOrDefault(predicate) is { } found)
                    return found;
                await Task.Delay(100);
            }

            throw new Xunit.Sdk.XunitException("No matching log event was written.");
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
    }
}
