using Intelibill.Api.Services.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Intelibill.Api.Hubs;

/// <summary>
/// Cache-invalidation notices for one shop: what changed, never the changed data itself.
/// </summary>
/// <remarks>
/// Admission and release are the gate's, not this class's: both hubs owe the same authority check,
/// and two copies of it would eventually disagree. There is deliberately no client-callable method —
/// a connection's scope is whatever the server admitted it under and nothing a client sends can
/// widen it.
/// </remarks>
[Authorize]
public sealed class ShopUpdatesHub(RealtimeConnectionGate gate) : Hub
{
    public override Task OnConnectedAsync() => gate.AdmitAsync(Context, nameof(ShopUpdatesHub));

    public override Task OnDisconnectedAsync(Exception? exception) => gate.ReleaseAsync(Context);
}
