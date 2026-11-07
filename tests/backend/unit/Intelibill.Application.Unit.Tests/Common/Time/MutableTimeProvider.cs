using System.Globalization;

namespace Intelibill.Application.Unit.Tests.Common.Time;

/// <summary>A <see cref="TimeProvider"/> whose instant a test sets; defaults to the real time.</summary>
internal sealed class MutableTimeProvider(DateTimeOffset? now = null) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now ?? DateTimeOffset.UtcNow;

    public static MutableTimeProvider At(string utc) =>
        new(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture));

    public override DateTimeOffset GetUtcNow() => Now;
}
