using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using Intelibill.Shared.Web.PublicErrors;
using Intelibill.Shared.Web.Logging;
using Intelibill.Shared.Web.Middleware;
using Intelibill.Api.Unit.Tests.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Serilog.Events;

namespace Intelibill.Api.Unit.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private const string BearerSentinel = "eyJsentinel-bearer-7f3a";
    private const string DatabasePasswordSentinel = "db-password-sentinel-91c2";
    private const string EmailSentinel = "sentinel.person@example.test";
    private const string InnerSentinel = "inner-sentinel-55d0";
    private const string DataSentinel = "data-sentinel-0be4";
    private const string PasswordSentinel = "Hunter2-password-sentinel";

    [Fact]
    public async Task InvokeAsync_WhenValidationException_ReturnsProblemJsonWithBadRequest()
    {
        var exception = new ValidationException([
            new ValidationFailure("Email", "Email is required")
        ]);

        var result = await InvokeAsync(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, result.Context.Response.StatusCode);
        Assert.Equal("application/problem+json", result.Context.Response.ContentType);
        Assert.Contains("validation errors", result.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", result.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Email is required", result.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Validation.Failed", result.Json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ValidationError_DoesNotEchoPasswordValue()
    {
        var exception = new ValidationException([
            new ValidationFailure("Password", $"'{PasswordSentinel}' is not a strong enough password.")
            {
                AttemptedValue = PasswordSentinel,
            },
            new ValidationFailure("Email", "Email is required") { AttemptedValue = "" },
        ]);

        var result = await InvokeAsync(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, result.Context.Response.StatusCode);
        Assert.DoesNotContain(PasswordSentinel, result.Payload, StringComparison.Ordinal);
        var errors = result.Json.GetProperty("errors").EnumerateArray().ToArray();
        Assert.Equal(2, errors.Length);
        // The field stays named and the caller still learns it was refused; only the text that
        // repeated the secret is replaced. An ordinary authored message survives untouched.
        Assert.Equal("Password", errors[0].GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(errors[0].GetProperty("description").GetString()));
        Assert.Equal("Email is required", errors[1].GetProperty("description").GetString());
        Assert.DoesNotContain(PasswordSentinel, result.Log.RenderedMessages(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArgumentException_UnexpectedTextIsNotPublic()
    {
        var result = await InvokeAsync(new ArgumentException($"Host=db;Password={DatabasePasswordSentinel}", "connection"));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Context.Response.StatusCode);
        Assert.Equal("application/problem+json", result.Context.Response.ContentType);
        Assert.Equal("Request.Invalid", result.Json.GetProperty("code").GetString());
        Assert.DoesNotContain(DatabasePasswordSentinel, result.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("connection", result.Payload, StringComparison.Ordinal);
        Assert.False(result.Json.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task InvokeAsync_WhenUnauthorizedAccessException_ReturnsUnauthorized()
    {
        var result = await InvokeAsync(new UnauthorizedAccessException($"token {BearerSentinel} rejected"));

        Assert.Equal(StatusCodes.Status401Unauthorized, result.Context.Response.StatusCode);
        Assert.Equal("application/problem+json", result.Context.Response.ContentType);
        Assert.Contains("Unauthorized", result.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Auth.Unauthorized", result.Json.GetProperty("code").GetString());
        Assert.DoesNotContain(BearerSentinel, result.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnexpectedException_ReturnsCodeAndTraceWithoutSentinel()
    {
        var result = await InvokeAsync(SecretBearingException());

        Assert.Equal(StatusCodes.Status500InternalServerError, result.Context.Response.StatusCode);
        Assert.Equal("application/problem+json", result.Context.Response.ContentType);
        Assert.Equal("Internal.Unexpected", result.Json.GetProperty("code").GetString());
        Assert.Equal(500, result.Json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(result.Json.GetProperty("traceId").GetString()));
        Assert.False(result.Json.TryGetProperty("detail", out _));
        AssertNoSentinel(result.Payload);
        Assert.DoesNotContain(nameof(InvalidOperationException), result.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(TimeoutException), result.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", result.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnexpectedException_WritesOneSafeDiagnosticUnderTheResponseTraceId()
    {
        var result = await InvokeAsync(SecretBearingException());

        var traceId = result.Json.GetProperty("traceId").GetString();
        var diagnostic = Assert.Single(result.Log.Events, e => e.Level == LogEventLevel.Error);
        Assert.Null(diagnostic.Exception);
        Assert.Equal(traceId, Scalar(diagnostic, "TraceId"));
        Assert.Equal("Internal.Unexpected", Scalar(diagnostic, "ErrorCode"));
        Assert.Contains(typeof(InvalidOperationException).FullName!, result.Log.RenderedCompact(), StringComparison.Ordinal);
        Assert.Contains(typeof(TimeoutException).FullName!, result.Log.RenderedCompact(), StringComparison.Ordinal);
        AssertNoSentinel(result.Log.RenderedCompact());
        AssertNoSentinel(result.Log.RenderedMessages());
    }

    [Fact]
    public async Task RequestCancellation_DoesNotLogError()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        var result = await InvokeAsync(
            _ => throw new OperationCanceledException(aborted.Token),
            context => context.Features.Set<IHttpRequestLifetimeFeature>(new RecordingLifetime(aborted.Token)));

        Assert.DoesNotContain(result.Log.Events, e => e.Level >= LogEventLevel.Warning);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, result.Context.Response.StatusCode);
        Assert.Equal(string.Empty, result.Payload);
    }

    [Fact]
    public async Task CallerDisconnect_SurfacingAsIOException_DoesNotLogError()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        // Kestrel reports a reset connection on the next write as an IOException, not a cancellation.
        var result = await InvokeAsync(
            _ => throw new IOException("The client reset the request stream."),
            context => context.Features.Set<IHttpRequestLifetimeFeature>(new RecordingLifetime(aborted.Token)));

        Assert.DoesNotContain(result.Log.Events, e => e.Level >= LogEventLevel.Warning);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, result.Context.Response.StatusCode);
    }

    [Fact]
    public async Task StartedNonFramedResponse_AbortsWithoutSecondJson()
    {
        var lifetime = new RecordingLifetime(CancellationToken.None);
        const string partial = "{\"type\":\"batch\",\"batch\":{\"itemName\":\"Ri";

        var result = await InvokeAsync(
            async context =>
            {
                await context.Response.WriteAsync(partial);
                throw SecretBearingException();
            },
            context =>
            {
                context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
                context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
            });

        Assert.True(lifetime.Aborted);
        Assert.Equal(partial, result.Payload);
        var diagnostic = Assert.Single(result.Log.Events, e => e.Level == LogEventLevel.Error);
        Assert.Equal("True", Scalar(diagnostic, "ResponseStarted"));
        AssertNoSentinel(result.Log.RenderedCompact());
    }

    [Fact]
    public async Task HostileTraceId_IsReplacedAndCannotInjectLogLines()
    {
        const string hostile = "abc\r\n{\"@t\":\"2026-01-01\",\"@mt\":\"forged admin login\",\"@l\":\"Fatal\"}";

        var result = await InvokeAsync(
            SecretBearingException(),
            context => context.TraceIdentifier = hostile);

        var traceId = result.Json.GetProperty("traceId").GetString()!;
        Assert.Matches(new Regex("^[0-9a-f]{32}$"), traceId);
        Assert.DoesNotContain("forged", result.Payload, StringComparison.Ordinal);

        var rendered = result.Log.RenderedCompact();
        Assert.DoesNotContain("forged", rendered, StringComparison.Ordinal);
        // One event, one line: nothing the caller sent can start a second record.
        Assert.Equal(result.Log.Events.Count, rendered.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(traceId, Scalar(Assert.Single(result.Log.Events), "TraceId"));
    }

    [Fact]
    public async Task WellFormedTraceIdentifier_IsKept()
    {
        var result = await InvokeAsync(
            SecretBearingException(),
            context => context.TraceIdentifier = "0HNA1B2C3D4E5:00000001");

        Assert.Equal("0HNA1B2C3D4E5:00000001", result.Json.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task RequestActivity_SuppliesTheTraceId()
    {
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();

        var result = await InvokeAsync(
            SecretBearingException(),
            context => context.TraceIdentifier = "0HNA1B2C3D4E5:00000001");

        Assert.Equal(activity.TraceId.ToHexString(), result.Json.GetProperty("traceId").GetString());
    }

    private static InvalidOperationException SecretBearingException()
    {
        var exception = new InvalidOperationException(
            $"Authorization: Bearer {BearerSentinel}; Host=db;Password={DatabasePasswordSentinel}; user {EmailSentinel}",
            new TimeoutException($"inner {InnerSentinel}"));
        exception.Data["secret"] = DataSentinel;
        return exception;
    }

    private static void AssertNoSentinel(string text)
    {
        foreach (var sentinel in new[] { BearerSentinel, DatabasePasswordSentinel, EmailSentinel, InnerSentinel, DataSentinel })
            Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
    }

    private static string? Scalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar
            ? scalar.Value?.ToString()
            : null;

    private static Task<InvocationResult> InvokeAsync(Exception exception, Action<HttpContext>? arrange = null) =>
        InvokeAsync(_ => throw exception, arrange);

    private static async Task<InvocationResult> InvokeAsync(RequestDelegate next, Action<HttpContext>? arrange = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        arrange?.Invoke(context);

        var log = new CapturedLog();
        var middleware = new ExceptionHandlingMiddleware(
            next,
            new SafeErrorFactory(),
            new SafeDiagnosticWriter(log.CreateLogger<SafeDiagnosticWriter>()));

        // Thrown for real, so the exception carries a stack trace the way a production one does.
        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();

        return new InvocationResult(context, payload, log);
    }

    private sealed record InvocationResult(HttpContext Context, string Payload, CapturedLog Log)
    {
        public JsonElement Json => JsonDocument.Parse(Payload).RootElement;
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class RecordingLifetime(CancellationToken requestAborted) : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; } = requestAborted;

        public bool Aborted { get; private set; }

        public void Abort() => Aborted = true;
    }
}
