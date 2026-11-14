using System.Diagnostics;
using System.Globalization;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0107Tests
{
    [Fact]
    public async Task BootstrapCommand_StillPreparesAfterInvitationServicesAreRegistered()
    {
        var runtime = await NewDatabaseAsync();
        var migrator = database.Login(runtime.Database!, LicensingPostgreSqlFixture.MigratorRole);
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (var argument in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Intelibill.Licensing.Api.dll"), "bootstrap-admin", "prepare",
            "--verified-email", "fixture.owner@example.test", "--verification-reference", "P01-07-fixture-attestation",
            "--admin-origin", "http://localhost:4001",
        })
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["LicensingMigrationDatabase__Host"] = migrator.Host;
        start.Environment["LicensingMigrationDatabase__Port"] = migrator.Port.ToString(CultureInfo.InvariantCulture);
        start.Environment["LicensingMigrationDatabase__Database"] = migrator.Database;
        start.Environment["LicensingMigrationDatabase__Username"] = migrator.Username;
        start.Environment["LicensingMigrationDatabase__Password"] = migrator.Password;

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));

        Assert.True(process.ExitCode == 0, "bootstrap-admin exited " + process.ExitCode + ": " + await errors);
        Assert.True(BootstrapValidationShape((await output).Trim()));
        Assert.Equal(1L, await ScalarAsync(runtime, "SELECT count(*) FROM initial_admin_bootstrap WHERE prepared_at IS NOT NULL"));
    }

    private static bool BootstrapValidationShape(string proof) =>
        Application.Bootstrap.BootstrapValidation.IsProofValid(proof);
}
