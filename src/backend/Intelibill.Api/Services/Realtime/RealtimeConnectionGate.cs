using System.Globalization;
using System.Security.Claims;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Realtime;
using Intelibill.Application.Common.Security;
using Microsoft.AspNetCore.SignalR;

namespace Intelibill.Api.Services.Realtime;

/// <summary>
/// What every notification transport is admitted through. A signed token proves only that this
/// server issued it; whether it still entitles the holder to a shop's events is decided here,
/// against the live session, account and membership, before any registration exists.
/// </summary>
/// <remarks>
/// Shared by both hubs so that there is one admission rule rather than two that can drift apart. No
/// SignalR group is joined: a group is a broadcast address, and the whole point of this change is
/// that a payload is addressed to re-authorized connection ids instead.
/// </remarks>
public sealed partial class RealtimeConnectionGate(
    ILiveSessionAuthority authority,
    IRealtimeConnectionRegistry registry,
    RealtimeConnectionTracker tracker,
    RealtimeInstanceIdentity instance,
    TimeProvider clock,
    ILogger<RealtimeConnectionGate> logger)
{
    public async Task AdmitAsync(HubCallerContext context, string hubName)
    {
        ArgumentNullException.ThrowIfNull(context);

        var claims = SessionClaims.TryRead(context.User);

        // A transport is always scoped to one shop. Without a shop claim there is nothing to
        // subscribe to, and inventing one from the account's memberships would hand the client a
        // scope the server never issued.
        if (claims?.ActiveShopId is null)
        {
            LogRefused(logger, hubName, "the token names no session or no active shop");
            context.Abort();
            return;
        }

        var expiresAt = ReadExpiry(context.User);
        var now = clock.GetUtcNow();
        if (expiresAt is null || expiresAt <= now)
        {
            LogRefused(logger, hubName, "the token carries no usable expiry");
            context.Abort();
            return;
        }

        bool authorized;
        try
        {
            authorized = await authority.IsValidAsync(claims, context.ConnectionAborted);
        }
        catch (OperationCanceledException)
        {
            context.Abort();
            return;
        }
        catch (Exception ex)
        {
            // Fail closed. A store that cannot say whether this is still allowed has not said yes.
            LogAuthorityUnavailable(logger, hubName, ex);
            context.Abort();
            return;
        }

        if (!authorized)
        {
            LogRefused(logger, hubName, "the session, account or membership is no longer live");
            context.Abort();
            return;
        }

        var connection = new RealtimeConnection(
            context.ConnectionId,
            instance.Value,
            hubName,
            claims.UserId,
            claims.SessionId,
            claims.ActiveShopId.Value,
            claims.ActiveShopRole,
            claims.SecurityVersion,
            claims.ScopeVersion,
            expiresAt.Value);

        // Tracked before the lease exists, not after. A sweep landing between the two would find the
        // lease, fail the authority check, delete the row and have nothing to abort - leaving a
        // socket open that neither dispatch nor any later sweep can see again.
        tracker.Track(connection, context);

        try
        {
            // Not the connection's own token: a registration cancelled mid-insert can still commit,
            // and the lease would then outlive a connect that nothing is going to release.
            await registry.RegisterAsync(connection, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // An unregistered transport is one no dispatcher can authorize and no sweep can find.
            // Leaving it open would be an invisible subscription, so it is refused instead.
            tracker.Forget(context.ConnectionId);
            LogRegistrationFailed(logger, hubName, ex);
            context.Abort();
            return;
        }
    }

    public async Task ReleaseAsync(HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        tracker.Forget(context.ConnectionId);

        try
        {
            await registry.RemoveAsync(context.ConnectionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A lease that outlives its socket is not a delivery risk: the dispatcher sends to a
            // connection id the transport layer no longer knows, and the sweep removes the row at
            // expiry. Worth a warning, not worth failing the disconnect.
            LogReleaseFailed(logger, context.ConnectionId, ex);
        }
    }

    /// <summary>
    /// The presented token's <c>exp</c>, which is what bounds the transport's life. Read from the
    /// principal rather than from configuration, so a token minted with a different lifetime is
    /// bounded by its own claim.
    /// </summary>
    private static DateTimeOffset? ReadExpiry(ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst("exp")?.Value;

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused a {HubName} connection: {Reason}.")]
    private static partial void LogRefused(ILogger logger, string hubName, string reason);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Refused a {HubName} connection: the live authority store could not be reached.")]
    private static partial void LogAuthorityUnavailable(ILogger logger, string hubName, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Refused a {HubName} connection: its lease could not be recorded.")]
    private static partial void LogRegistrationFailed(ILogger logger, string hubName, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Lease for connection {ConnectionId} outlived its socket; it will be removed at expiry.")]
    private static partial void LogReleaseFailed(ILogger logger, string connectionId, Exception exception);
}
