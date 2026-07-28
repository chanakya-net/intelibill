using Intelibill.Infrastructure.Data;

namespace Intelibill.Api.Startup;

internal sealed class ApplicationModelWarmupService(
    IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _ = context.Model;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static class ApplicationModelWarmupServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationModelWarmup(this IServiceCollection services)
    {
        services.AddHostedService<ApplicationModelWarmupService>();
        return services;
    }
}
