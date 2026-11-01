using Intelibill.Api.Services.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Intelibill.Api.Hubs;

/// <summary>
/// Catalog change notifications for one shop.
/// </summary>
/// <remarks>
/// Admission and release are the gate's, not this class's: both hubs owe the same authority check,
/// and two copies of it would eventually disagree. There is deliberately no client-callable method —
/// a connection's scope is whatever the server admitted it under and nothing a client sends can
/// widen it.
/// </remarks>
[Authorize]
public sealed class ProductHub(RealtimeConnectionGate gate) : Hub
{
    public override Task OnConnectedAsync() => gate.AdmitAsync(Context, nameof(ProductHub));

    public override Task OnDisconnectedAsync(Exception? exception) => gate.ReleaseAsync(Context);
}
