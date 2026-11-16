using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Intelibill.Shared.Web.Delivery;

public interface ILeasedHttpDestination
{
    bool Enabled { get; }
    Task QueueDueAsync(CancellationToken ct);
    Task<string?> SendAsync(LeasedHttpMessage message, CancellationToken ct);
}

public sealed class LeasedHttpDeliveryPump(LeasedHttpOutbox outbox, ILeasedHttpDestination destination,
    [FromKeyedServices(LeasedHttpDeliveryPump.LeaseClockKey)] TimeProvider clock)
{
    public const string LeaseClockKey = "entitlement-delivery-lease";
    public const string ClientName = "EntitlementDelivery";

    public async Task<bool> DeliverOneAsync(CancellationToken ct)
    {
        if (!destination.Enabled) return false;
        var lease = Guid.NewGuid();
        var message = await outbox.ClaimAsync(clock.GetUtcNow(), lease, ct);
        if (message is null) return false;

        var failure = await SendAsync(message, ct);
        await outbox.FinishAsync(message.EventId, lease, clock.GetUtcNow(), failure is null,
            message.Kind == "Snapshot", failure, ct);
        return true;
    }

    public Task QueueDueAsync(CancellationToken ct) =>
        destination.Enabled ? destination.QueueDueAsync(ct) : Task.CompletedTask;

    private async Task<string?> SendAsync(LeasedHttpMessage message, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            return await destination.SendAsync(message, timeout.Token);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException)
        {
            ct.ThrowIfCancellationRequested();
            return "Unavailable";
        }
    }

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, Uri origin, string path,
        byte[] body, string assertion, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, path)) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", assertion);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    public static void Register(IServiceCollection services)
    {
        services.TryAddKeyedSingleton<TimeProvider>(LeaseClockKey,
            (provider, _) => provider.GetRequiredService<TimeProvider>());
        services.AddScoped<LeasedHttpDeliveryPump>();
        services.AddHostedService<LeasedHttpDeliveryWorker>();
        services.AddHttpClient(ClientName).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            MaxResponseHeadersLength = 8,
            ActivityHeadersPropagator = null,
        });
    }
}

public sealed partial class LeasedHttpDeliveryWorker(IServiceScopeFactory scopes, ILogger<LeasedHttpDeliveryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var pump = scope.ServiceProvider.GetRequiredService<LeasedHttpDeliveryPump>();
                await pump.QueueDueAsync(stoppingToken);
                for (var index = 0; index < 10 && await pump.DeliverOneAsync(stoppingToken); index++) { }
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                LogDeliveryFailure(logger, error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Entitlement worker unavailable ({Cause}).")]
    private static partial void LogDeliveryFailure(ILogger logger, string cause);
}
