using Intelibill.Application.Common.Interfaces;

namespace Intelibill.Api.Services.Realtime;

internal static class RealtimeAuthorizationExtensions
{
    /// <summary>
    /// Everything that decides who may hold a notification transport and who a payload may reach.
    /// </summary>
    public static IServiceCollection AddRealtimeAuthorization(this IServiceCollection services)
    {
        // Singletons: the instance name identifies the process for its whole life, and the tracker
        // holds sockets that outlive any request scope.
        services.AddSingleton<RealtimeInstanceIdentity>();
        services.AddSingleton<RealtimeConnectionTracker>();

        // Scoped: admission and dispatch both read live authority through the request's DbContext.
        services.AddScoped<RealtimeConnectionGate>();
        services.AddScoped(
            typeof(IAuthorizedRealtimeDispatcher<>),
            typeof(AuthorizedRealtimeDispatcher<>));

        services.AddScoped<IProductHubNotifier, SignalRProductHubNotifier>();
        services.AddScoped<IShopUpdatesNotifier, SignalRShopUpdatesNotifier>();

        services.AddHostedService<RealtimeRevocationWorker>();

        return services;
    }
}
