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

/// <summary>
/// Database wait budgets for API hosts under test.
/// </summary>
/// <remarks>
/// Production gives a lock 3 s (<c>DatabaseLocks:Brief</c>) or 5 s (<c>DatabaseLocks:Standard</c>)
/// and the licensing connection 5 s per command. Concurrency tests queue several requests behind a
/// held lock and release it only once all of them wait. On a loaded runner the next request can take
/// longer than 3 s to reach the lock, so the first waiter gave up early and the test saw an outage
/// instead of contention. Tests that check the timeout itself build their own context, which keeps
/// the production defaults, or set <c>lock_timeout</c> explicitly.
/// </remarks>
internal static class TestDatabaseBudgets
{
    public static IReadOnlyDictionary<string, string?> Merchant { get; } = new Dictionary<string, string?>
    {
        ["DatabaseLocks:Brief"] = "00:00:20",
        ["DatabaseLocks:Standard"] = "00:00:25",
    };

    public static IReadOnlyDictionary<string, string?> Licensing { get; } = new Dictionary<string, string?>
    {
        ["DatabaseLocks:Brief"] = "00:00:20",
        ["DatabaseLocks:Standard"] = "00:00:25",
        // The command timeout must outlast the lock budget, or the command is cancelled first.
        ["LicensingDatabase:TimeoutSeconds"] = "30",
    };
}
