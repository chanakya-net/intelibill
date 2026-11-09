using System.Diagnostics;

namespace Intelibill.Integration.Tests;

/// <summary>Local diagnostic samples, not a memory/capacity contract. Excludes fixture construction.</summary>
internal sealed class ReadMemoryObservation : IDisposable
{
    private readonly Timer _timer;
    private long _managed;
    private long _resident;
    public ReadMemoryObservation()
    {
        Sample();
        _timer = new Timer(_ => Sample(), null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
    }
    public long PeakManagedBytes { get { Sample(); return Interlocked.Read(ref _managed); } }
    public long PeakResidentBytes { get { Sample(); return Interlocked.Read(ref _resident); } }
    private void Sample()
    {
        SetMaximum(ref _managed, GC.GetTotalMemory(false));
        using var process = Process.GetCurrentProcess();
        SetMaximum(ref _resident, process.WorkingSet64);
    }
    private static void SetMaximum(ref long location, long value)
    {
        var previous = Interlocked.Read(ref location);
        while (value > previous)
        {
            var actual = Interlocked.CompareExchange(ref location, value, previous);
            if (actual == previous) return;
            previous = actual;
        }
    }
    public void Dispose() => _timer.Dispose();
}
