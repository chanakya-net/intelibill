using Intelibill.Licensing.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelibill.Licensing.Infrastructure.Notifications;

public sealed class NotificationPlanningOptions
{
    public const string SectionName = "Notifications";

    public bool PlanInBackground { get; init; } = true;
    public int IntervalMilliseconds { get; init; } = 5000;
    public int AccountPageSize { get; init; } = 20;
    public int MaxAccountsPerTick { get; init; } = 100;
    public int TermPageSize { get; init; } = 50;
    public int MemberPageSize { get; init; } = 50;

    internal TimeSpan EffectiveInterval =>
        TimeSpan.FromMilliseconds(Math.Clamp(IntervalMilliseconds, 50, 300_000));
}

internal sealed partial class NotificationPlanningWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationPlanningOptions> options,
    ILogger<NotificationPlanningWorker> logger) : BackgroundService
{
    private Guid? _nextAfterAccount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PlanInBackground)
            return;

        using var timer = new PeriodicTimer(options.Value.EffectiveInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPassSafeAsync(stoppingToken);
        }
    }

    private async Task RunPassSafeAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var authority = scope.ServiceProvider.GetRequiredService<IMerchantNoticeRecipientAuthority>();
            if (!authority.Enabled)
                return;

            var planner = scope.ServiceProvider.GetRequiredService<NotificationPlanner>();
            var settings = options.Value;
            var batch = await planner.PlanBatchAsync(
                afterAccount: _nextAfterAccount,
                maxAccounts: Math.Clamp(settings.MaxAccountsPerTick, 1, 1000),
                accountPageSize: Math.Clamp(settings.AccountPageSize, 1, 100),
                termPageSize: Math.Clamp(settings.TermPageSize, 1, 100),
                memberPageSize: Math.Clamp(settings.MemberPageSize, 1, 50),
                ct: ct);
            _nextAfterAccount = batch.NextAfterAccount;
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            LogPlanningUnavailable(logger, error.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification planning pass skipped ({Cause}).")]
    private static partial void LogPlanningUnavailable(ILogger logger, string cause);
}
