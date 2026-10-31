using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Intelibill.Infrastructure.Services.Auth;

internal sealed partial class IdentityMailWorker(IServiceScopeFactory scopes, ILogger<IdentityMailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await DeliverOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { LogDeliveryFailure(logger); } // Never log exceptions carrying URLs, payloads or SMTP secrets.
        }
    }
    internal async Task DeliverOneAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IEmailOwnershipRepository>();
        var queue = scope.ServiceProvider.GetRequiredService<IdentityMailOutbox>();
        if (!queue.Enabled) return;
        IdentityMail? mail;
        await using (var transaction = await repository.BeginAsync(ct))
        {
            var now = DateTimeOffset.UtcNow;
            mail = await db.Set<IdentityMail>().Where(x => x.CompletedAt == null && x.NextAttemptAt <= now && x.Attempts < 5)
                .OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
            if (mail is null) return;
            // Each id is resolved against its own table. Treating an invitation id as a challenge
            // id made every lookup miss, and the miss was read as "superseded" and discarded.
            var challenge = mail.ChallengeId is { } challengeId ? await repository.FindAsync(challengeId, ct) : null;
            var superseded = mail.ChallengeId is not null && (challenge is null || challenge.ConsumedAt is not null);
            if (mail.InvitationId is { } invitationId)
            {
                var invitation = await db.Set<ShopInvitation>().FirstOrDefaultAsync(x => x.Id == invitationId, ct);
                superseded = invitation is null || !invitation.IsPending(now);
            }

            if (mail.ExpiresAt <= now || superseded)
            { mail.Complete(now); await repository.CommitAsync(ct); return; }
            mail.Failed(now); // Durable bounded lease; 60s first attempt > the 30s SMTP timeout.
            await repository.CommitAsync(ct);
        }
        // No advisory/database transaction is held across SMTP network I/O.
        var content = queue.Decrypt(mail);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await scope.ServiceProvider.GetRequiredService<IEmailService>().SendIdentityAsync(mail.Destination, content.Subject, content.Body, timeout.Token);
        mail.Complete(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
    }
    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity email delivery failed; durable bounded retry remains scheduled. Inspect queue attempt counts.")]
    private static partial void LogDeliveryFailure(ILogger logger);
}
