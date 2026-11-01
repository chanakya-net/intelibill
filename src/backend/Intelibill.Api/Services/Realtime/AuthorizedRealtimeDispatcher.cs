using Intelibill.Application.Common.Interfaces;
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
        IReadOnlyList<Application.Common.Realtime.RealtimeConnection> candidates;
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

        var now = clock.GetUtcNow();
        var authorized = new List<string>(candidates.Count);

        // Several tabs on one device are several connections under one session, and they ask the
        // authority the same question. SessionClaims is a record, so equality dedupes them and a
        // mutation costs one lookup per distinct authority rather than one per socket.
        var decisions = new Dictionary<SessionClaims, bool>();

        foreach (var candidate in candidates)
        {
            // Decisive without a lookup, and true even for a lease the sweep has not reached yet.
            if (candidate.ExpiresAt <= now)
                continue;

            var claims = candidate.ToSessionClaims();
            if (!decisions.TryGetValue(claims, out var allowed))
            {
                try
                {
                    allowed = await authority.IsValidAsync(claims, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Stop checking, but still deliver to the recipients already confirmed. Each of
                    // them passed a complete live check, so sending costs nothing in exposure, and
                    // dropping the event for the whole shop would punish every proven recipient for
                    // one failed lookup. Failing closed means never sending to an id that was not
                    // validated - not refusing to send to the ones that were.
                    LogAuthorityUnavailable(logger, eventName, HubName, authorized.Count, ex);
                    break;
                }

                decisions[claims] = allowed;
            }

            if (allowed)
                authorized.Add(candidate.ConnectionId);
        }

        if (authorized.Count == 0)
            return;

        await hubContext.Clients.Clients(authorized).SendAsync(eventName, payload, cancellationToken);
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
