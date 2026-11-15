using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Intelibill.Shared.Web.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Xunit.Abstractions;

namespace Intelibill.P0102ConsumerRegressionTests;

/// <summary>
/// Hosts the real merchant and licensing APIs against real PostgreSQL, plants credentials, a
/// database password and a customer's email in the same failure inside each, then reads
/// everything that left each host — HTTP body, every log event as a sink received it and every
/// span as an exporter would see it — and requires the same safe outcome from both.
/// </summary>
[Collection(SharedHostsGroup.Name)]
public sealed partial class P0102ConsumerRegressionTests(SharedPostgreSqlFixture database, ITestOutputHelper output)
{
    private static readonly string[] EnvelopeProperties = ["code", "status", "title", "traceId", "type"];

    [Fact]
    public async Task BothApis_AnswerIdenticalSafeProblem()
    {
        var problems = new List<JsonElement>();
        foreach (var host in Hosts(maskPii: true))
        {
            using (host)
            {
                var (status, mediaType, body) = await ThrowAsync(host);
                output.WriteLine($"{host.Name}: {(int)status} {mediaType} {body}");

                Assert.Equal(HttpStatusCode.InternalServerError, status);
                Assert.Equal("application/problem+json", mediaType);
                Sentinels.AssertNone(body);
                var problem = JsonDocument.Parse(body).RootElement.Clone();
                Assert.Equal(EnvelopeProperties, problem.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
                Assert.Equal("Internal.Unexpected", problem.GetProperty("code").GetString());
                Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
                problems.Add(problem);
            }
        }

        Assert.Equal(Comparable(problems[0]), Comparable(problems[1]));
    }

    [Fact]
    public async Task BothApis_SinksNeverReceiveExceptionTextOrSecrets()
    {
        foreach (var host in Hosts(maskPii: true))
        {
            using (host)
            {
                var (_, _, body) = await ThrowAsync(host);
                var traceId = JsonDocument.Parse(body).RootElement.GetProperty("traceId").GetString();
                var diagnostic = await host.Sink.WaitForAsync(
                    e => CapturingSink.Scalar(e, "TraceId") == traceId && CapturingSink.Scalar(e, "ErrorCode") == "Internal.Unexpected",
                    $"{host.Name} request diagnostic");
                Assert.Equal(LogEventLevel.Error, diagnostic.Level);
                Assert.Null(diagnostic.Exception);
                Assert.True(diagnostic.Properties.ContainsKey("ExceptionTypes"));

                LogNestedPayload(host);
                var payload = await host.Sink.WaitForAsync(e => e.Properties.ContainsKey("Payload"), $"{host.Name} nested payload");
                Assert.Contains(LogEventRedactor.Masked, payload.Properties["Payload"].ToString(), StringComparison.Ordinal);

                var rendered = host.Sink.Rendered();
                output.WriteLine($"{host.Name} sink: {rendered}");
                Sentinels.AssertNone(rendered);
            }
        }
    }

    [Fact]
    public async Task BothApis_BackgroundWorkerAndAuditFailuresReachSinksRedacted()
    {
        foreach (var host in Hosts(maskPii: true))
        {
            using (host)
            {
                // Resolving a service boots the host, which starts the worker.
                var loggers = host.Services.GetRequiredService<ILoggerFactory>();
                var worker = await host.Sink.WaitForAsync(
                    e => e.MessageTemplate.Text.StartsWith(WorkerProbe.Marker, StringComparison.Ordinal),
                    $"{host.Name} hosted worker failure");
                Assert.Null(worker.Exception);
                Assert.True(worker.Properties.ContainsKey("ExceptionFrames"));

                var audit = loggers.CreateLogger("Intelibill.P0102.Audit");
                LogAuditFailure(audit, Sentinels.SecretBearingException(), Sentinels.Email, Sentinels.NestedRefresh);
                var auditEvent = await host.Sink.WaitForAsync(
                    e => CapturingSink.Scalar(e, "SourceContext") == "Intelibill.P0102.Audit", $"{host.Name} audit failure");
                Assert.Null(auditEvent.Exception);

                var rendered = host.Sink.Rendered();
                output.WriteLine($"{host.Name} worker/audit sink: {rendered}");
                Sentinels.AssertNone(rendered);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PiiMasking_FollowsHostConfiguration(bool maskPii)
    {
        foreach (var host in Hosts(maskPii))
        {
            using (host)
            {
                var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Intelibill.P0102.Pii");
                LogContact(logger, Sentinels.Email, Sentinels.Email, Sentinels.NestedToken);
                var contact = await host.Sink.WaitForAsync(e => e.Properties.ContainsKey("CustomerEmail"), $"{host.Name} contact");

                var rendered = host.Sink.Rendered();
                output.WriteLine($"{host.Name} (mask PII {maskPii}): {rendered}");
                Sentinels.AssertNone(rendered, includingEmail: maskPii);
                Assert.Equal(maskPii ? LogEventRedactor.Masked : Sentinels.Email, CapturingSink.Scalar(contact, "CustomerEmail"));
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothApis_MfaCredentialsAreMaskedIndependentlyOfPii(bool maskPii)
    {
        var payload = new
        {
            Challenge = new
            {
                TotpCode = "p0102-totp-sentinel",
                RecoveryCode = "p0102-recovery-sentinel",
                RecoveryCodes = new[] { "p0102-recovery-collection-sentinel" },
                ErrorCode = "Internal.Unexpected",
                StatusCode = 500,
            },
            Form = new Dictionary<string, object>
            {
                ["totp_code"] = "p0102-totp-form-sentinel",
                ["recovery-code"] = "p0102-recovery-form-sentinel",
                ["recovery_codes"] = new[] { "p0102-recovery-form-collection-sentinel" },
            },
        };
        foreach (var host in Hosts(maskPii))
        {
            using (host)
            {
                var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Intelibill.P0102.Mfa");
                LogPayload(logger, payload, null);
                var log = await host.Sink.WaitForAsync(e => e.Properties.ContainsKey("Payload"), $"{host.Name} MFA payload");
                AssertMaskedMfa(log.Properties["Payload"]);
                var rendered = host.Sink.Rendered();
                Assert.DoesNotContain("p0102-totp-", rendered, StringComparison.Ordinal);
                Assert.DoesNotContain("p0102-recovery-", rendered, StringComparison.Ordinal);
            }
        }
    }

    private static void AssertMaskedMfa(LogEventPropertyValue payload)
    {
        var properties = Assert.IsType<StructureValue>(payload).Properties.ToDictionary(p => p.Name, p => p.Value);
        var challenge = Assert.IsType<StructureValue>(properties["Challenge"]).Properties.ToDictionary(p => p.Name, p => p.Value);
        var form = Assert.IsType<DictionaryValue>(properties["Form"]).Elements;
        string[] challengeFields = ["TotpCode", "RecoveryCode", "RecoveryCodes"];
        string[] formFields = ["totp_code", "recovery-code", "recovery_codes"];

        Assert.All(challengeFields, name => Assert.Equal(LogEventRedactor.Masked, Assert.IsType<ScalarValue>(challenge[name]).Value));
        Assert.All(formFields, name => Assert.Equal(LogEventRedactor.Masked, Assert.IsType<ScalarValue>(form[new ScalarValue(name)]).Value));
        Assert.Equal("Internal.Unexpected", Assert.IsType<ScalarValue>(challenge["ErrorCode"]).Value);
        Assert.Equal(500, Assert.IsType<ScalarValue>(challenge["StatusCode"]).Value);
    }

    [Fact]
    public async Task Licensing_MixedCaseLoggingLevelStillEmitsRequestDiagnostic()
    {
        using var host = new LicensingHost(database.LicensingRuntime, maskPii: true, minimumLevel: "eRrOr");
        var (status, _, body) = await ThrowAsync(host);
        Assert.Equal(HttpStatusCode.InternalServerError, status);
        using var problem = JsonDocument.Parse(body);
        var traceId = problem.RootElement.GetProperty("traceId").GetString();
        var diagnostic = await host.Sink.WaitForAsync(
            e => CapturingSink.Scalar(e, "TraceId") == traceId && CapturingSink.Scalar(e, "ErrorCode") == "Internal.Unexpected",
            "licensing request diagnostic at mixed-case Error level");
        Assert.Equal(LogEventLevel.Error, diagnostic.Level);
    }

    [Fact]
    public async Task BothApis_ExportedSpansDropExceptionText()
    {
        foreach (var host in Hosts(maskPii: true))
        {
            using (host)
            {
                var (_, _, body) = await ThrowAsync(host);
                var traceId = JsonDocument.Parse(body).RootElement.GetProperty("traceId").GetString()!;
                var span = await host.Spans.WaitForServerSpanAsync(traceId);
                output.WriteLine($"{host.Name} span: status {span.Status}, description '{span.StatusDescription}', tags {string.Join(',', span.Tags.Keys)}");

                Assert.Equal(ActivityStatusCode.Error, span.Status);
                Assert.Null(span.StatusDescription);
                Assert.False(span.Tags.ContainsKey("exception.message"));
                Assert.False(span.Tags.ContainsKey("exception.stacktrace"));
                Assert.Equal(typeof(InvalidOperationException).FullName, span.Tags["exception.type"]);
            }
        }
    }

    private IHostUnderTest[] Hosts(bool maskPii) =>
    [
        new MerchantHost(database.MerchantRuntime, maskPii),
        new LicensingHost(database.LicensingRuntime, maskPii),
    ];

    private static async Task<(HttpStatusCode Status, string? MediaType, string Body)> ThrowAsync(IHostUnderTest host)
    {
        using var client = host.CreateHttpsClient();
        using var response = await client.GetAsync(new Uri(FailureProbe.Path, UriKind.Relative));
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync());
    }

    private static string Comparable(JsonElement problem) => JsonSerializer.Serialize(
        problem.EnumerateObject().Where(p => p.Name != "traceId").ToDictionary(p => p.Name, p => p.Value.ToString()));

    private static void LogNestedPayload(IHostUnderTest host)
    {
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Intelibill.P0102.Inbound");
        var payload = new
        {
            Request = new
            {
                Headers = new Dictionary<string, string> { ["Authorization"] = Sentinels.NestedHeader },
                Body = new { AccessToken = Sentinels.NestedToken, Password = Sentinels.Password },
            },
            Form = new Dictionary<string, object> { ["refresh_token"] = Sentinels.NestedRefresh },
        };
        LogPayload(logger, payload, null);
    }

    private static readonly Action<ILogger, object, Exception?> LogPayload =
        LoggerMessage.Define<object>(LogLevel.Information, new EventId(1, "P0102Inbound"), "Inbound {@Payload}");

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit append failed for {CustomerEmail} with {RefreshToken}")]
    private static partial void LogAuditFailure(ILogger logger, Exception exception, string customerEmail, string refreshToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Contact {CustomerEmail} wrote from {Address} with {AccessToken}")]
    private static partial void LogContact(ILogger logger, string customerEmail, string address, string accessToken);
}
