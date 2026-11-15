using System.Net;
using System.Text.Json;
using Intelibill.Integration.Tests;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

var factory = new ApiWebApplicationFactory();
try
{
    factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
    await factory.InitializeAsync();
    factory.StartServer();
    var origin = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    Reply(new { origin });

    while (await Console.In.ReadLineAsync() is { } line)
    {
        using var message = JsonDocument.Parse(line);
        var request = message.RootElement;
        switch (request.GetProperty("command").GetString())
        {
            case "mailbox":
                Reply(await OnboardingBrowserProbe.ReadEnrollmentAsync(factory, request.GetProperty("email").GetString()!));
                break;
            case "snapshot":
                Reply(await OnboardingBrowserProbe.ReadSnapshotAsync(factory.MigratorConnectionString, request.GetProperty("shopId").GetGuid()));
                break;
            case "stop":
                return;
            default:
                throw new InvalidOperationException("Unknown onboarding fixture command.");
        }
    }
}
finally
{
    await factory.DisposeAsync();
    await ((IAsyncLifetime)factory).DisposeAsync();
}

static void Reply<T>(T value) =>
    Console.WriteLine($"ONBOARDING {JsonSerializer.Serialize(value)}");
