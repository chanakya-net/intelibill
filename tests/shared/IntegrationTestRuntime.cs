namespace Intelibill.Tests.Integration;

/// <summary>
/// How many test classes run at the same time.
/// </summary>
/// <remarks>
/// Every running class keeps an API host and a database busy, so the limit follows
/// the machine: one class per processor, as many as its memory allows, never more
/// than eight. Past eight a developer machine gains nothing, because the database
/// container becomes the bottleneck. The memory allowance is what a running class
/// was measured to cost the test host: about 5 GB when each host compiles its
/// Wolverine handlers at run time, about 1.5 GB with pre-generated handlers
/// (<see cref="PregeneratedHandlers"/>). A 4-processor, 16 GB CI runner therefore
/// runs four classes with pre-generated handlers and three without.
/// <c>INTEGRATION_TEST_PARALLELISM</c> overrides the number, and <c>1</c> restores a
/// strictly serial run.
/// </remarks>
internal static class IntegrationParallelism
{
    private const int Ceiling = 8;
    private const double GigabytesPerClassCompiling = 5;
    private const double GigabytesPerClassPregenerated = 1.5;

    public static int Degree { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("INTEGRATION_TEST_PARALLELISM"), out var configured) && configured > 0
            ? configured
            : Math.Clamp(Math.Min(Environment.ProcessorCount, ClassesThatFitInMemory()), 1, Ceiling);

    public static SemaphoreSlim Slots { get; } = new(Degree, Degree);

    private static int ClassesThatFitInMemory()
    {
        var gigabytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024d * 1024 * 1024);
        var perClass = PregeneratedHandlers.Enabled ? GigabytesPerClassPregenerated : GigabytesPerClassCompiling;
        return (int)(gigabytes / perClass);
    }
}

/// <summary>
/// Whether the API under test loads pre-built Wolverine handler classes.
/// </summary>
/// <remarks>
/// Off unless <c>INTEGRATION_TEST_PREGENERATED_HANDLERS=1</c>. Only the script that
/// has just regenerated the handler code and rebuilt the API sets it, so the
/// pre-built classes can never be older than the handlers they wrap. A plain
/// <c>dotnet test</c> compiles handlers at run time, which is slower and always
/// current.
/// </remarks>
internal static class PregeneratedHandlers
{
    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("INTEGRATION_TEST_PREGENERATED_HANDLERS") == "1";
}
