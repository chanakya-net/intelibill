namespace Intelibill.Licensing.Integration.Tests;

// The design-time test temporarily configures the environment and working directory.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LicensingDatabaseTests
{
    internal const string Name = "Licensing PostgreSQL";
}
