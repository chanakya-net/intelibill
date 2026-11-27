using Intelibill.Infrastructure.Observability;

namespace Intelibill.Api.Unit.Tests.Infrastructure.Observability;

public sealed class RequestDatabaseTelemetryTests
{
    [Fact]
    public void RecordCommand_AccumulatesDurationAndCount()
    {
        var telemetry = new RequestDatabaseTelemetry();

        telemetry.RecordCommand(TimeSpan.FromMilliseconds(12.5));
        telemetry.RecordCommand(TimeSpan.FromMilliseconds(7.5));

        Assert.Equal(TimeSpan.FromMilliseconds(20), telemetry.CumulativeDuration);
        Assert.Equal(2, telemetry.CommandCount);
    }

    [Fact]
    public async Task BeginRequest_IsolatesConcurrentLogicalRequestFlows()
    {
        var accessor = new RequestDatabaseTelemetryAccessor();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = RecordInRequestAsync(TimeSpan.FromMilliseconds(11), release.Task);
        var second = RecordInRequestAsync(TimeSpan.FromMilliseconds(37), release.Task);
        release.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(TimeSpan.FromMilliseconds(11), results[0].CumulativeDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(37), results[1].CumulativeDuration);
        Assert.Null(accessor.Current);

        async Task<RequestDatabaseTelemetry> RecordInRequestAsync(
            TimeSpan duration,
            Task waitForRelease)
        {
            using var request = accessor.BeginRequest();
            accessor.Current!.RecordCommand(duration);
            await waitForRelease;
            return request.Telemetry;
        }
    }
}
