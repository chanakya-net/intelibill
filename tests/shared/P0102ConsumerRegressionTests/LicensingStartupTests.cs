using System.Diagnostics;
using Xunit.Abstractions;

namespace Intelibill.P0102ConsumerRegressionTests;

public sealed class LicensingStartupTests(ITestOutputHelper output)
{
    private const string StartupFailure = "Licensing host failed";

    [Theory]
    [InlineData("Observability__Logging__AsyncBufferSize", "p0102-startup-logging-sentinel", "InvalidOperationException")]
    [InlineData("LicensingDatabase__Port", "p0102-startup-database-sentinel", "InvalidOperationException")]
    [InlineData("Observability__Logging__MinimumLevel", "99", "OptionsValidationException")]
    public async Task InvalidSettings_ExitWithSafeDiagnostics(string setting, string value, string exceptionType)
    {
        using var process = Process.Start(StartInfo(setting, value))
            ?? throw new InvalidOperationException("The licensing executable did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException($"The licensing executable did not reject {setting} within 10 seconds.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }

        Assert.NotEqual(0, process.ExitCode);
        var streams = await Task.WhenAll(stdout, stderr);
        foreach (var stream in streams)
        {
            Assert.DoesNotContain("p0102-startup-", stream, StringComparison.Ordinal);
            Assert.DoesNotContain($"'{value}'", stream, StringComparison.Ordinal);
            Assert.DoesNotContain($"\"{value}\"", stream, StringComparison.Ordinal);
            Assert.DoesNotContain("Failed to convert configuration value", stream, StringComparison.Ordinal);
            Assert.DoesNotContain("is not a valid value", stream, StringComparison.Ordinal);
            Assert.DoesNotContain("Observability:Logging needs", stream, StringComparison.Ordinal);
            Assert.DoesNotContain("Unhandled exception", stream, StringComparison.Ordinal);
        }

        Assert.Equal($"{StartupFailure} ({exceptionType}).", streams[1].Trim());
        output.WriteLine($"Exit {process.ExitCode}. stdout: {streams[0]} stderr: {streams[1]}");
    }

    private static ProcessStartInfo StartInfo(string setting, string value)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Path.Combine(HostProbes.Root, "src/licensing/Intelibill.Licensing.Api"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(Intelibill.Licensing.Api.Program).Assembly.Location);
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        start.Environment["LicensingDatabase__Host"] = "127.0.0.1";
        start.Environment["LicensingDatabase__Database"] = "p0102_fixture";
        start.Environment["LicensingDatabase__Username"] = "p0102_fixture";
        start.Environment["LicensingDatabase__Password"] = "p0102-startup-password-sentinel";
        start.Environment[setting] = value;
        return start;
    }
}
