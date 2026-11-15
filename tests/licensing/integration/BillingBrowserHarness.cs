using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>Loopback web-server and owned-process helpers shared by the billing browser harnesses.</summary>
internal static class BillingBrowserHarness
{
    internal static async Task<OwnedProcess> StartMerchantWebAsync(string root, Uri origin, CancellationToken cancellationToken)
    {
        var start = NodeStart(root, origin);
        var script = Path.Combine(root, "dist/INVENTORY/server/server.mjs");
        Assert.True(File.Exists(script), "Build the shipping merchant web server first.");
        start.ArgumentList.Add(script);
        start.Environment["WEB_BIND_HOST"] = "127.0.0.1";
        return await StartWebAsync(start, cancellationToken);
    }

    internal static ProcessStartInfo NodeStart(string root, Uri origin) => new("node")
    {
        WorkingDirectory = root, RedirectStandardOutput = true,
        Environment = { ["PORT"] = "0", ["API_ORIGIN"] = origin.GetLeftPart(UriPartial.Authority) },
    };

    internal static async Task<OwnedProcess> StartWebAsync(ProcessStartInfo start, CancellationToken cancellationToken)
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
    internal sealed class MerchantWebBuild : IDisposable
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

    internal sealed class OwnedProcess(ProcessStartInfo start) : IAsyncDisposable
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

    internal sealed class InternalTraffic : IStartupFilter
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
