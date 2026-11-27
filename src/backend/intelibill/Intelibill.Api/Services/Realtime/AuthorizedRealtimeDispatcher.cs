using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Realtime;
using Intelibill.Application.Common.Security;
using Microsoft.AspNetCore.SignalR;

namespace Intelibill.Api.Services.Realtime;

/// <summary>
/// Sends a shop event to the connections that may still receive it.
/// </summary>
public interface IAuthorizedRealtimeDispatcher<THub>
    where THub : Hub
{
    Task SendShopAsync(
        Guid shopId,
        string eventName,
        object payload,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What replaced the group broadcast. A group is an address, not a permission: once a connection is
/// in one, every later event reaches it for as long as the socket lives. Here each payload resolves
/// its own recipients from the lease registry and re-checks the live authority behind each of them,
/// so a membership withdrawn a second after a socket opened stops the next event rather than the
/// next sign-in.
/// </summary>
/// <remarks>
/// <para>
/// Fail closed. If the registry or the authority store cannot answer, nothing is sent — and in
/// particular nothing is sent to the group, which would be the one failure mode worth avoiding at
/// any cost. Clients treat these events as advisory cache invalidations, so a dropped event costs a
/// staleness window, not data.
/// </para>
/// <para>
/// A payload authorized a moment before a revocation commits may already be in flight and cannot be
/// recalled. What this class guarantees is the other side of that line: after the revocation is
/// visible, no new delivery decision comes out yes.
/// </para>
/// </remarks>
internal sealed partial class AuthorizedRealtimeDispatcher<THub>(
    IHubContext<THub> hubContext,
    IRealtimeConnectionRegistry registry,
    ILiveSessionAuthority authority,
    TimeProvider clock,
    ISuspensionAccessReader suspensions,
    ILogger<AuthorizedRealtimeDispatcher<THub>> logger) : IAuthorizedRealtimeDispatcher<THub>
    where THub : Hub
{
    private static readonly string HubName = typeof(THub).Name;

    public async Task SendShopAsync(
        Guid shopId,
        string eventName,
        object payload,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RealtimeConnection> candidates;
        try
        {
            candidates = await registry.ListShopConnectionsAsync(HubName, shopId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogRecipientsUnresolvable(logger, eventName, HubName, ex);
            return;
        }

        if (candidates.Count == 0)
            return;

        try
        {
            List<string> recipients = [];
            await suspensions.DeliverAsync(shopId, null,
                ct => recipients.Count == 0 ? Task.CompletedTask : hubContext.Clients.Clients(recipients).SendAsync(eventName, payload, ct),
                cancellationToken,
                prepare: async ct => recipients = await ResolveRecipientsAsync(candidates, eventName, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogRecipientsUnresolvable(logger, eventName, HubName, ex);
        }
    }

    private async Task<List<string>> ResolveRecipientsAsync(
        IReadOnlyList<RealtimeConnection> candidates, string eventName, CancellationToken ct)
    {
        var authorized = new List<RealtimeConnection>(candidates.Count);
        // Tabs on the same session share one live authority check per event.
        var decisions = new Dictionary<SessionClaims, bool>();
        var now = clock.GetUtcNow();

        foreach (var candidate in candidates)
        {
            if (candidate.ExpiresAt <= now)
                continue;

            var claims = candidate.ToSessionClaims();
            if (!decisions.TryGetValue(claims, out var allowed))
            {
                try
                {
                    allowed = await authority.IsValidAsync(claims, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Keep already-confirmed recipients if a later authority lookup fails.
                    LogAuthorityUnavailable(logger, eventName, HubName, authorized.Count, ex);
                    break;
                }

                decisions[claims] = allowed;
            }

            if (allowed)
                authorized.Add(candidate);
        }

        var handoffAt = clock.GetUtcNow();
        return authorized.Where(candidate => candidate.ExpiresAt > handoffAt)
            .Select(candidate => candidate.ConnectionId).ToList();
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Dropped {EventName} on {HubName}: its recipients could not be resolved. No broadcast fallback was used.")]
    private static partial void LogRecipientsUnresolvable(
        ILogger logger,
        string eventName,
        string hubName,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Cut {EventName} on {HubName} short: the live authority store stopped answering after {ConfirmedRecipients} recipient(s) were confirmed. Only those were sent to.")]
    private static partial void LogAuthorityUnavailable(
        ILogger logger,
        string eventName,
        string hubName,
        int confirmedRecipients,
        Exception exception);
}
