using System.Text;
using Intelibill.Api.Extensions;
using Intelibill.Shared.Web.Logging;
using Microsoft.Extensions.Logging;
using Npgsql;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace Intelibill.Api.Unit.Tests.Logging;

public sealed class SensitiveLoggingTests
{
    private const string PasswordSentinel = "password-sentinel-a1";
    private const string AccessTokenSentinel = "access-token-sentinel-b2";
    private const string BearerSentinel = "bearer-sentinel-c3";
    private const string RefreshSentinel = "refresh-sentinel-d4";
    private const string CodeSentinel = "code-sentinel-e5";
    private const string CookieSentinel = "cookie-sentinel-f6";
    private const string ConnectionSentinel = "connection-sentinel-a7";
    private const string ResetTokenSentinel = "reset-token-sentinel-b8";
    private const string MessageSentinel = "message-sentinel-c9";
    private const string DataSentinel = "data-sentinel-d0";
    private const string InnerSentinel = "inner-sentinel-e1";
    private const string EmailSentinel = "sentinel.customer@example.test";
    private static readonly string[] ConflictCodes = ["Sale.Conflict"];

    [Fact]
    public void NestedSensitiveFields_AreRedactedAtEveryDepth()
    {
        using var log = new CapturedLog();

        log.Logger.Information("Inbound {@Payload}", new
        {
            User = new
            {
                Name = "Ravi Kumar",
                Password = PasswordSentinel,
                Sessions = new[] { new { AccessToken = AccessTokenSentinel, Device = "Pixel" } },
            },
            Headers = new Dictionary<string, object>
            {
                ["Authorization"] = $"Bearer {BearerSentinel}",
                ["Cookie"] = $"sid={CookieSentinel}",
                ["X-Shop"] = "shop-visible",
                ["Nested"] = new Dictionary<string, string> { ["refresh_token"] = RefreshSentinel },
            },
            Challenges = new List<object> { new { Code = CodeSentinel, Purpose = "login" } },
            ConnectionString = $"Host=db;Password={ConnectionSentinel}",
        });

        var rendered = log.RenderedCompact() + log.RenderedMessages();
        AssertNone(rendered, PasswordSentinel, AccessTokenSentinel, BearerSentinel, RefreshSentinel, CodeSentinel, CookieSentinel, ConnectionSentinel);
        Assert.Contains("Ravi Kumar", rendered, StringComparison.Ordinal);
        Assert.Contains("Pixel", rendered, StringComparison.Ordinal);
        Assert.Contains("shop-visible", rendered, StringComparison.Ordinal);
        Assert.Contains("login", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionDataAndMessage_DoNotReachSink()
    {
        using var log = new CapturedLog();
        var exception = Thrown();

        log.Logger.Error(exception, "Request failed on {Route}", "api/sales");
#pragma warning disable CA1848 // The point is a plain Microsoft.Extensions.Logging call, as library code makes.
        log.CreateLogger<SensitiveLoggingTests>().LogError(exception, "Worker failed on {Route}", "identity-mail");
#pragma warning restore CA1848
        // An exception passed as an ordinary value would otherwise render through ToString().
        log.Logger.Warning("Retrying after {Failure}", exception);

        Assert.All(log.Events, e => Assert.Null(e.Exception));
        var rendered = log.RenderedCompact() + log.RenderedMessages();
        AssertNone(rendered, MessageSentinel, DataSentinel, InnerSentinel, BearerSentinel);
        // What is kept is enough to find the code: the types and where they were thrown.
        Assert.Contains(typeof(InvalidOperationException).FullName!, rendered, StringComparison.Ordinal);
        Assert.Contains(typeof(TimeoutException).FullName!, rendered, StringComparison.Ordinal);
        Assert.Contains($"{typeof(SensitiveLoggingTests).FullName}.{nameof(Thrown)}", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetLink_IsAbsentFromRenderedLogs()
    {
        using var log = new CapturedLog();

        log.Logger.Information("Delivering {ResetLink}", $"https://app.test/reset-password?token={ResetTokenSentinel}");
        log.Logger.Information("Queued mail {Url}", new Uri($"https://app.test/auth/verify-email#challengeId=42&token={ResetTokenSentinel}&purpose=signup"));
        log.Logger.Information("Opened {Address}", $"https://app.test/reset-password?code={CodeSentinel}&lang=en");
#pragma warning disable CA1848, CA2254 // Interpolated text in place of a template is the case under test.
        log.CreateLogger<SensitiveLoggingTests>().LogWarning($"Delivery failed for https://app.test/reset-password?token={ResetTokenSentinel}");
#pragma warning restore CA1848, CA2254

        var rendered = log.RenderedCompact() + log.RenderedMessages();
        AssertNone(rendered, ResetTokenSentinel, CodeSentinel);
        Assert.Contains("challengeId=42", rendered, StringComparison.Ordinal);
        Assert.Contains("lang=en", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BearerAndConnectionSecrets_AreScrubbedFromFreeText()
    {
        using var log = new CapturedLog();

        log.Logger.Information("Upstream said {Reply}", $"401 for Authorization: Bearer {BearerSentinel}; retry with Host=db;Password={ConnectionSentinel};Pooling=true");
        log.Logger.Information("Saw {Dto}", new LoginAttempt("ravi@example.test", PasswordSentinel));

        var rendered = log.RenderedCompact() + log.RenderedMessages();
        AssertNone(rendered, BearerSentinel, ConnectionSentinel, PasswordSentinel);
        Assert.Contains("Pooling=true", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void PersonalData_IsMaskedOnlyWhenThePolicyAsks()
    {
        using var masked = new CapturedLog(maskPersonalData: true);
        using var plain = new CapturedLog(maskPersonalData: false);

        foreach (var log in new[] { masked, plain })
        {
            log.Logger.Information("Reset requested for {Email} {Phone} by {Note}", EmailSentinel, "+919876543210", $"customer {EmailSentinel}");
            log.Logger.Information("Signed in with {Password}", PasswordSentinel);
        }

        AssertNone(masked.RenderedCompact(), EmailSentinel, "+919876543210", PasswordSentinel);
        Assert.Contains(EmailSentinel, plain.RenderedCompact(), StringComparison.Ordinal);
        // Credentials are never optional.
        Assert.DoesNotContain(PasswordSentinel, plain.RenderedCompact(), StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorAndStatusCodes_AreNotMistakenForSecrets()
    {
        using var log = new CapturedLog();

        log.Logger.Warning("Request failed with {StatusCode} {ErrorCode} {Codes}", 409, "Sale.Conflict", ConflictCodes);

        var rendered = log.RenderedCompact();
        Assert.Contains("409", rendered, StringComparison.Ordinal);
        Assert.Contains("Sale.Conflict", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void DeepAndWideValues_AreBounded()
    {
        using var log = new CapturedLog();
        object deep = new { Leaf = "bottom" };
        for (var i = 0; i < 40; i++)
            deep = new { Child = deep };

        log.Logger.Information("Deep {@Value}", deep);
        log.Logger.Information("Wide {Values}", Enumerable.Range(0, 10_000).ToArray());
        log.Logger.Information("Long {Text}", new string('x', 100_000));

        var events = log.Events;
        Assert.Equal(3, events.Count);
        var rendered = log.RenderedCompact();
        Assert.DoesNotContain("bottom", rendered, StringComparison.Ordinal);
        Assert.Contains(LogEventRedactor.Truncated, rendered, StringComparison.Ordinal);
        Assert.True(((SequenceValue)events[1].Properties["Values"]).Elements.Count <= LogEventRedactor.MaxElements + 1);
        Assert.True(rendered.Length < 60_000, $"rendered {rendered.Length} characters");
    }

    [Fact]
    public void DatabaseExceptionText_DoesNotReachSink()
    {
        using var log = new CapturedLog();
        var database = new Npgsql.PostgresException(
            $"invalid input syntax for type uuid: \"{MessageSentinel}\"", "ERROR", "ERROR", "22P02");
        var wrapped = new InvalidOperationException($"query failed near {DataSentinel}", database);

        // EF Core's QueryIterationFailed puts the exception into the message as an argument.
        log.Logger.Error("An exception occurred while iterating for context type '{ContextType}'.{NewLine}{Error}", "ApplicationDbContext", Environment.NewLine, database);
        log.Logger.Error("Wrapped {Error}", wrapped);
#pragma warning disable CA1848, CA2254 // Exception text in place of a template is the case under test.
        log.CreateLogger<SensitiveLoggingTests>().LogError(database.ToString());
#pragma warning restore CA1848, CA2254

        var rendered = log.RenderedCompact() + log.RenderedMessages();
        AssertNone(rendered, MessageSentinel, DataSentinel);
        Assert.Contains("Npgsql.PostgresException", rendered, StringComparison.Ordinal);
        Assert.Contains("ApplicationDbContext", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionTypeNames_AreKeptVerbatim()
    {
        using var log = new CapturedLog();

        log.Logger.Error(Thrown(), "Failed");
        log.Logger.Warning("Retry after {FailureType}", nameof(NpgsqlException));

        var events = log.Events;
        var types = (SequenceValue)events[0].Properties["ExceptionTypes"];
        Assert.Equal(typeof(InvalidOperationException).FullName, ((ScalarValue)types.Elements[0]).Value);
        Assert.Equal(nameof(NpgsqlException), ((ScalarValue)events[1].Properties["FailureType"]).Value);
    }

    [Fact]
    public void HeaderLists_MaskCredentialValuesByKey()
    {
        using var log = new CapturedLog();
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Authorization", $"Bearer {BearerSentinel}"),
            new("Password", PasswordSentinel),
            new("X-Trace", "trace-visible"),
        };

        log.Logger.Information("Destructured {@Headers}", headers);
        log.Logger.Information("Plain {Headers}", headers);

        var rendered = log.RenderedCompact() + log.RenderedMessages();
        AssertNone(rendered, BearerSentinel, PasswordSentinel);
        Assert.Contains("trace-visible", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void AppShapedSecretsInText_AreScrubbed()
    {
        using var log = new CapturedLog();

        log.Logger.Information("Body {Body}", $"{{\"refreshToken\":\"{RefreshSentinel}\",\"shopId\":\"shop-visible\"}}");
        log.Logger.Information("Query {Query}", $"accessToken={AccessTokenSentinel}&verificationCode={CodeSentinel}&page=2");
        log.Logger.Information("Form {Form}", $"newPassword={PasswordSentinel}; X-Api-Key: {ConnectionSentinel}");
        log.Logger.Information("Quoted {Connection}", $"Host=db;Password='a;{CookieSentinel}';Pooling=true");

        var rendered = log.RenderedCompact() + log.RenderedMessages();
        AssertNone(rendered, RefreshSentinel, AccessTokenSentinel, CodeSentinel, PasswordSentinel, ConnectionSentinel, CookieSentinel);
        Assert.Contains("shop-visible", rendered, StringComparison.Ordinal);
        Assert.Contains("page=2", rendered, StringComparison.Ordinal);
        Assert.Contains("Pooling=true", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryText_IsNotMistakenForCredentials()
    {
        using var log = new CapturedLog();

        log.Logger.Information("Sold {Item}", "Basic Rice 5kg");
        log.Logger.Warning("Database said {Reply}", "Error code: 23505, status code: 503");
        log.Logger.Warning("Scheme {Scheme}", "Bearer authentication failed");

        var rendered = log.RenderedCompact();
        Assert.Contains("Basic Rice 5kg", rendered, StringComparison.Ordinal);
        Assert.Contains("Error code: 23505, status code: 503", rendered, StringComparison.Ordinal);
        Assert.Contains("Bearer authentication failed", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void DictionaryKeysMaskedToTheSameValue_StillWriteTheEvent()
    {
        using var log = new CapturedLog(maskPersonalData: true);

        log.Logger.Information("Balances {Balances}", new Dictionary<string, decimal>
        {
            ["first.customer@example.test"] = 10m,
            ["second.customer@example.test"] = 20m,
        });

        var logEvent = Assert.Single(log.Events);
        Assert.DoesNotContain("customer@example.test", log.RenderedCompact(), StringComparison.Ordinal);
        Assert.IsType<DictionaryValue>(logEvent.Properties["Balances"]);
    }

    [Fact]
    public async Task ExporterPayload_CarriesNoSentinel()
    {
        var handler = new CapturingHandler();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Redacted(new LogEventRedactor(maskPersonalData: true), redacted => redacted.OpenTelemetry(options =>
            {
                options.Endpoint = "http://otlp.invalid/v1/logs";
                options.Protocol = OtlpProtocol.HttpProtobuf;
                options.HttpMessageHandler = handler;
            }))
            .CreateLogger();

        logger.Error(Thrown(), "Export probe {Marker} for {Email} with {@Session}", "exporter-marker", EmailSentinel, new { RefreshToken = RefreshSentinel });
        await logger.DisposeAsync();

        var payload = Encoding.UTF8.GetString(handler.Bodies.SelectMany(b => b).ToArray());
        Assert.Contains("exporter-marker", payload, StringComparison.Ordinal);
        AssertNone(payload, MessageSentinel, DataSentinel, InnerSentinel, BearerSentinel, EmailSentinel, RefreshSentinel);
    }

    private static InvalidOperationException Thrown()
    {
        try
        {
            try
            {
                throw new TimeoutException($"inner {InnerSentinel}");
            }
            catch (TimeoutException inner)
            {
                var outer = new InvalidOperationException($"failed with {MessageSentinel} Bearer {BearerSentinel}", inner);
                outer.Data["credential"] = DataSentinel;
                throw outer;
            }
        }
        catch (InvalidOperationException caught)
        {
            return caught;
        }
    }

    private static void AssertNone(string text, params string[] sentinels)
    {
        foreach (var sentinel in sentinels)
            Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
    }

    private sealed record LoginAttempt(string Login, string Password);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<byte[]> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
