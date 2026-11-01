using Intelibill.Application.Common.Interfaces;

namespace Intelibill.Api.Services.Realtime;

/// <summary>
/// Closes notification sockets whose authority has gone, and clears the leases they left behind.
/// </summary>
/// <remarks>
/// <para>
/// A sweep rather than a revocation signal. A signal has to be written at every site that can end
/// access — a password change, a session revoke, a revoke-all, a role change, a status change, a
/// shop switch, a recovery-epoch bump — and each site that forgets is a hole nobody notices. This
/// re-asks the live authority about every lease the process holds, so there is no signal to lose and
/// no call site to keep in step.
/// </para>
/// <para>
/// It is not what makes delivery safe. <see cref="AuthorizedRealtimeDispatcher{THub}"/> authorizes
/// every payload, so a socket this sweep has not yet reached receives nothing in the meantime. That
/// is why a store that cannot answer leaves the socket open instead of disconnecting everyone: the
/// cost of waiting is a socket, and the cost of failing closed here would be a disconnect storm on
/// every transient blip.
/// </para>
/// </remarks>
internal sealed partial class RealtimeRevocationWorker(
    IServiceScopeFactory scopes,
    RealtimeConnectionTracker tracker,
    RealtimeInstanceIdentity instance,
    TimeProvider clock,
    ILogger<RealtimeRevocationWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSweepFailed(logger, ex);
            }
        }
    }

    /// <summary>Runs one pass. Returns how many sockets this process actually closed.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRealtimeConnectionRegistry>();
        var authority = scope.ServiceProvider.GetRequiredService<ILiveSessionAuthority>();

        var now = clock.GetUtcNow();

        // Listed before anything is deleted. A lease past its token expiry is precisely the one
        // whose socket should be closed, so purging first would hide it from the loop below and
        // leave this backstop existing only on paper.
        //
        // Only this process's leases: another replica's socket cannot be closed from here, and
        // deleting its lease would take away the row it needs in order to find and close it.
        var held = await registry.ListInstanceConnectionsAsync(instance.Value, cancellationToken);

        var closed = 0;
        foreach (var connection in held)
        {
            bool stillAllowed;
            try
            {
                stillAllowed = connection.ExpiresAt > now
                    && await authority.IsValidAsync(connection.ToSessionClaims(), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogAuthorityUnavailable(logger, connection.ConnectionId, ex);
                continue;
            }

            if (stillAllowed)
                continue;

            if (tracker.Abort(connection.ConnectionId))
                closed++;

            await registry.RemoveAsync(connection.ConnectionId, cancellationToken);
        }

        if (closed > 0)
            LogClosed(logger, closed);

        // Last, and guarded on its own. This removes what processes that are gone left behind, which
        // is bookkeeping — coupling it to the revocation above would let one broken DELETE keep every
        // revoked socket on this replica open indefinitely.
        //
        // The grace period is what keeps it from reaching across replicas: a lease still inside it
        // belongs to an instance that has not had its own sweep yet, and that instance is the only
        // one that can close the socket behind it.
        try
        {
            await registry.RemoveExpiredAsync(now - (SweepInterval * 2), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogCleanupFailed(logger, ex);
        }

        return closed;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Closed {Count} realtime connection(s) whose access had been withdrawn.")]
    private static partial void LogClosed(ILogger logger, int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not re-check connection {ConnectionId}; it stays open until the next sweep. No payload reaches it meanwhile, because dispatch authorizes every recipient.")]
    private static partial void LogAuthorityUnavailable(ILogger logger, string connectionId, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not remove abandoned realtime leases; revocation was unaffected and the next sweep retries.")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A realtime revocation sweep failed; the next one is already scheduled.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
