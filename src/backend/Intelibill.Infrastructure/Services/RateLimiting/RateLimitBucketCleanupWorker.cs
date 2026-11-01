using Intelibill.Application.Common.Interfaces;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Services.RateLimiting;

/// <summary>
/// Keeps <c>rate_limit_buckets</c> to the buckets that still decide something. Every replica runs
/// one; the delete skips rows another replica or an acquisition holds, so they never contend.
/// A bucket left behind only costs space: an acquisition resets an ended window itself.
/// </summary>
internal sealed partial class RateLimitBucketCleanupWorker(
    IAtomicRateLimitStore store,
    IOptions<RateLimitStoreOptions> options,
    TimeProvider clock,
    ILogger<RateLimitBucketCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CleanupInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await DeleteExpiredAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            // Type only: a provider message can quote the connection or the statement.
            catch (Exception exception) { LogCleanupFailure(logger, exception.GetType().Name); }
        }
    }

    internal async Task DeleteExpiredAsync(CancellationToken ct)
    {
        var batchSize = options.Value.CleanupBatchSize;
        int deleted;
        do
        {
            deleted = await store.DeleteExpiredAsync(clock.GetUtcNow(), batchSize, ct);
        }
        while (deleted == batchSize);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit bucket cleanup failed ({Cause}); it will run again at the next interval.")]
    private static partial void LogCleanupFailure(ILogger logger, string cause);
}
