using System.Diagnostics;
using Intelibill.Shared.Web.Observability;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Observability;

public class SafeSpanProcessorTests
{
    private const string Sentinel = "invalid input syntax for type uuid: \"span-sentinel-77\"";

    [Fact]
    public void ErrorDescriptionAndExceptionTags_AreClearedBeforeExport()
    {
        using var activity = new Activity("db.query").Start();
        // What EF Core instrumentation does on a failed command.
        activity.SetStatus(ActivityStatusCode.Error, $"22P02: {Sentinel}");
        activity.SetTag("exception.message", Sentinel);
        activity.SetTag("exception.stacktrace", $"at Npgsql... {Sentinel}");
        activity.SetTag("exception.type", "Npgsql.PostgresException");
        activity.SetTag("db.system", "postgresql");

        new SafeSpanProcessor().OnEnd(activity);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Null(activity.StatusDescription);
        Assert.Null(activity.GetTagItem("exception.message"));
        Assert.Null(activity.GetTagItem("exception.stacktrace"));
        Assert.Equal("Npgsql.PostgresException", activity.GetTagItem("exception.type"));
        Assert.Equal("postgresql", activity.GetTagItem("db.system"));
    }

    [Fact]
    public void SuccessfulSpan_IsLeftAlone()
    {
        using var activity = new Activity("db.query").Start();
        activity.SetStatus(ActivityStatusCode.Ok);

        new SafeSpanProcessor().OnEnd(activity);

        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }
}
