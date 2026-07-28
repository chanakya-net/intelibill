namespace Intelibill.Infrastructure.Observability;

public sealed class RequestDatabaseTelemetry
{
    private long _cumulativeDurationTicks;
    private long _commandCount;

    public TimeSpan CumulativeDuration =>
        TimeSpan.FromTicks(Interlocked.Read(ref _cumulativeDurationTicks));

    public long CommandCount => Interlocked.Read(ref _commandCount);

    public void RecordCommand(TimeSpan duration)
    {
        Interlocked.Add(ref _cumulativeDurationTicks, duration.Ticks);
        Interlocked.Increment(ref _commandCount);
    }
}
