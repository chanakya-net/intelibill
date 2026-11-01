using Intelibill.Api.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Serilog.AspNetCore;
using Serilog.Events;

namespace Intelibill.Api.Unit.Tests.Extensions;

public class SerilogRequestLoggingTests
{
    [Fact]
    public void CallerCancellation_IsNotAnErrorRequest()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature { RequestAborted = aborted.Token });

        var level = LevelFor(context, new OperationCanceledException(aborted.Token));

        Assert.Equal(LogEventLevel.Information, level);
    }

    [Fact]
    public void ExceptionWhileTheCallerIsConnected_IsAnErrorRequest()
    {
        var level = LevelFor(new DefaultHttpContext(), new InvalidOperationException("boom"));

        Assert.Equal(LogEventLevel.Error, level);
    }

    private static LogEventLevel LevelFor(HttpContext context, Exception exception)
    {
        var options = new RequestLoggingOptions();
        SerilogExtensions.RequestLoggingOptions(options);
        return options.GetLevel(context, 1, exception);
    }
}
