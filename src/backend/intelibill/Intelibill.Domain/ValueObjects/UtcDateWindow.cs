namespace Intelibill.Domain.ValueObjects;

/// <summary>A half-open range of instants, <c>[StartInclusive, EndExclusive)</c>, in UTC.</summary>
public readonly record struct UtcDateWindow(DateTimeOffset StartInclusive, DateTimeOffset EndExclusive)
{
    public bool Contains(DateTimeOffset instant) => instant >= StartInclusive && instant < EndExclusive;
}
