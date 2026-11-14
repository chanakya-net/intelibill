using Intelibill.Licensing.Application;
using Intelibill.Licensing.Application.Bootstrap;
using Intelibill.Licensing.Infrastructure;
using Intelibill.Licensing.Infrastructure.Data;

namespace Intelibill.Licensing.Api.Bootstrap;

internal static class BootstrapAdminCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is not ["bootstrap-admin", "prepare", "--verified-email", var email,
            "--verification-reference", var reference, "--admin-origin", var origin])
        {
            Console.Error.WriteLine("Usage: bootstrap-admin prepare --verified-email EMAIL --verification-reference REFERENCE --admin-origin ORIGIN");
            return 2;
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddLicensingApplication().AddBootstrapPersistence();
        builder.Services.AddScoped(_ => new LicensingDbContextFactory().CreateDbContext([]));
        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<PrepareBootstrapAdminHandler>();
        var result = await handler.PrepareAsync(email, reference, origin,
            BootstrapHttp.AllowsLoopbackHttp(host.Services.GetRequiredService<IHostEnvironment>()), CancellationToken.None);
        if (result.IsError)
        {
            Console.Error.WriteLine(result.FirstError.Description);
            return 2;
        }

        // The operator redirects stdout to an access-controlled destination.
        Console.WriteLine(result.Value.Value);
        return 0;
    }
}
