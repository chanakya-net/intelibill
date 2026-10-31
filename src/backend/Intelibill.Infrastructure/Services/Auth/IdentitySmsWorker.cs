using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Intelibill.Infrastructure.Services.Auth;

/// <summary>
/// Delivers queued identity SMS. Mirrors the mail worker: a bounded lease is taken and committed
/// before the network call, so no database transaction is held across it, and a superseded or
/// expired message is dropped rather than sent to someone who no longer needs it.
/// </summary>
internal sealed partial class IdentitySmsWorker(IServiceScopeFactory scopes, ILogger<IdentitySmsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await DeliverOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            // Never log the exception itself: a provider client puts the request body, and so the
            // code and the full destination, into its message.
            catch (Exception) { LogDeliveryFailure(logger); }
        }
    }

    internal async Task DeliverOneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IPhoneChallengeRepository>();
        var queue = scope.ServiceProvider.GetRequiredService<IdentitySmsOutbox>();
        if (!queue.Enabled) return;

        IdentitySms? message;
        await using (var transaction = await repository.BeginAsync(ct))
        {
            var now = DateTimeOffset.UtcNow;
            message = await db.Set<IdentitySms>()
                .Where(x => x.CompletedAt == null && x.NextAttemptAt <= now && x.Attempts < 5)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (message is null) return;

            var challenge = message.ChallengeId is { } challengeId
                ? await repository.FindForUpdateAsync(challengeId, ct)
                : null;
            var superseded = message.ChallengeId is not null && (challenge is null || challenge.ConsumedAt is not null);

            if (message.ExpiresAt <= now || superseded)
            {
                message.Complete(now);
                await repository.CommitAsync(ct);
                return;
            }

            message.Failed(now);
            await repository.CommitAsync(ct);
        }

        var content = queue.Decrypt(message);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await scope.ServiceProvider.GetRequiredService<ISmsSender>().SendIdentityAsync(message.Destination, content.Body, timeout.Token);
        message.Complete(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity SMS delivery failed; durable bounded retry remains scheduled. Inspect queue attempt counts.")]
    private static partial void LogDeliveryFailure(ILogger logger);
}
