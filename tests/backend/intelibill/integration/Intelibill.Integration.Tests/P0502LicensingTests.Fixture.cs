using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0502LicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
    : OfflinePermitHttpFixture(fixture, output);
