using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0201BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [P0201BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MerchantOrigin_PreservesDelegationBoundary()
    {
        await database.PrepareMerchantAsync();
        using var fixture = await P0201Fixture.CreateAsync(database);
        var traffic = new InternalTraffic();
        await using var licensing = fixture.LicensingApi(configure: services =>
        {
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IStartupFilter>(traffic);
        });
        licensing.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var licensingClient = licensing.CreateClient();
        var licensingOrigin = licensingClient.BaseAddress!;
        await using var merchant = fixture.MerchantApi(licensingOrigin.AbsoluteUri);
        merchant.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        using var merchantClient = merchant.CreateClient();
        var actor = await P0201Tests.SeedMerchantAsync(merchant.Services);
        var allowed = Guid.NewGuid();
        await fixture.GrantAsync(allowed, actor);

        var root = ProjectPaths.Root;
        var merchantRoot = Path.Combine(root, "src/frontend");
        var adminRoot = Path.Combine(root, "src/admin-frontend");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var staging = new MerchantWebBuild(merchantRoot);
        await using var merchantWeb = await StartMerchantWebAsync(staging.Root, merchantClient.BaseAddress!, deadline.Token);
        await using var adminWeb = await StartAdminWebAsync(adminRoot, licensingOrigin, deadline.Token);
        var evidence = Directory.CreateTempSubdirectory("intelibill-p0201-").FullName;
        await RunPlaywrightAsync(adminRoot, merchantWeb.Origin, adminWeb.Origin, licensingOrigin,
            allowed, evidence, deadline.Token);

        Assert.Equal(3, traffic.Requests);
        output.WriteLine("Licensing observed exactly two server delegations and one direct rejected request.");
        output.WriteLine($"Sanitized browser output directory {evidence}");
    }

    private async Task RunPlaywrightAsync(string adminRoot, string merchantWeb, string adminWeb,
        Uri licensingOrigin, Guid account, string evidence, CancellationToken cancellationToken)
    {
        var playwright = new ProcessStartInfo("bun") { WorkingDirectory = adminRoot, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "x", "playwright", "test", "--config", "../../tests/licensing/browser/playwright.p0201.config.ts" })
            playwright.ArgumentList.Add(argument);
        playwright.Environment["NODE_PATH"] = Path.Combine(adminRoot, "node_modules");
        playwright.Environment["P0201_MERCHANT_WEB"] = merchantWeb;
        playwright.Environment["P0201_ADMIN_WEB"] = adminWeb;
        playwright.Environment["P0201_LICENSING_API"] = licensingOrigin.AbsoluteUri;
        playwright.Environment["P0201_ACCOUNT"] = account.ToString("D");
        playwright.Environment["P0201_DENIED_ACCOUNT"] = Guid.NewGuid().ToString("D");
        playwright.Environment["P0201_EMAIL"] = P0201Tests.Email;
        playwright.Environment["P0201_PASSWORD"] = P0201Tests.Password;
        playwright.Environment["P0201_EVIDENCE"] = evidence;

        await using var run = new OwnedProcess(playwright);
        var stdout = run.Process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = run.Process.StandardError.ReadToEndAsync(cancellationToken);
        await run.Process.WaitForExitAsync(cancellationToken);
        var report = await stdout;
        output.WriteLine(report);
        var errors = await stderr;
        var browserArtifacts = Path.Combine(evidence, "browser");
        if (Directory.Exists(browserArtifacts))
            Directory.Delete(browserArtifacts, recursive: true);

        Assert.True(run.Process.ExitCode == 0 && File.Exists(Path.Combine(evidence, "observations.json")), errors);
    }

    private static async Task<OwnedProcess> StartMerchantWebAsync(string root, Uri origin, CancellationToken cancellationToken)
    {
        var start = NodeStart(root, origin);
        var script = Path.Combine(root, "dist/INVENTORY/server/server.mjs");
        Assert.True(File.Exists(script), "Build the shipping merchant web server first.");
        start.ArgumentList.Add(script);
        start.Environment["WEB_BIND_HOST"] = "127.0.0.1";
        return await StartWebAsync(start, cancellationToken);
    }

    private static async Task<OwnedProcess> StartAdminWebAsync(string root, Uri origin, CancellationToken cancellationToken)
    {
        Assert.True(File.Exists(Path.Combine(root, "dist/ADMIN/browser/index.html")), "Build the shipping admin web first.");
        var start = NodeStart(root, origin);
        start.ArgumentList.Add("--input-type=module");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("""
            import { createAdminServer } from './server.mjs';
            const server = createAdminServer({ root: 'dist/ADMIN/browser', apiOrigin: process.env.API_ORIGIN });
            server.listen(0, '127.0.0.1', () => console.log('http://localhost:' + server.address().port));
            """);
        return await StartWebAsync(start, cancellationToken);
    }

    private static ProcessStartInfo NodeStart(string root, Uri origin) => new("node")
    {
        WorkingDirectory = root, RedirectStandardOutput = true,
        Environment = { ["PORT"] = "0", ["API_ORIGIN"] = origin.GetLeftPart(UriPartial.Authority) },
    };

    private static async Task<OwnedProcess> StartWebAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        var process = new OwnedProcess(start);
        try
        {
            while (await process.Process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                const string marker = "http://localhost:";
                var index = line.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 0 && int.TryParse(line.AsSpan(index + marker.Length), out var port) && port > 0)
                {
                    process.Origin = $"http://127.0.0.1:{port}";
                    return process;
                }
            }
            throw new InvalidOperationException("The shipping browser server exited before listening.");
        }
        catch
        {
            await process.DisposeAsync();
            throw;
        }
    }

    // Express ignores files whose absolute path traverses a dot-directory.
    // Stage the unchanged build as the shipping image does under /app/dist.
    private sealed class MerchantWebBuild : IDisposable
    {
        private readonly DirectoryInfo _staging = Directory.CreateTempSubdirectory("intelibill-web-p0201-");

        internal MerchantWebBuild(string root)
        {
            try
            {
                var build = Path.Combine(root, "dist/INVENTORY");
                Assert.True(File.Exists(Path.Combine(build, "server/server.mjs")), "Build the shipping merchant web server first.");
                foreach (var file in Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(Root, "dist/INVENTORY", Path.GetRelativePath(build, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }
            catch
            {
                _staging.Delete(recursive: true);
                throw;
            }
        }

        internal string Root => _staging.FullName;

        public void Dispose() => _staging.Delete(recursive: true);
    }

    private sealed class OwnedProcess(ProcessStartInfo start) : IAsyncDisposable
    {
        internal Process Process { get; } = Process.Start(start) ?? throw new InvalidOperationException("Process failed to start.");
        internal string Origin { get; set; } = string.Empty;

        public async ValueTask DisposeAsync()
        {
            using (Process)
            {
                if (!Process.HasExited)
                    Process.Kill(entireProcessTree: true);
                await Process.WaitForExitAsync();
            }
        }
    }

    private sealed class InternalTraffic : IStartupFilter
    {
        private int _requests;
        internal int Requests => Volatile.Read(ref _requests);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, pipeline) =>
            {
                if (context.Request.Path.StartsWithSegments("/internal/licensing/v1/customer"))
                    Interlocked.Increment(ref _requests);
                return pipeline(context);
            });
            next(app);
        };
    }
}

internal sealed class P0201BrowserFactAttribute : FactAttribute
{
    public P0201BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0201_RUN_BROWSER") != "1")
            Skip = "Set P0201_RUN_BROWSER=1 after building both shipping web applications.";
    }
}
