namespace Intelibill.Api.Services.Realtime;

/// <summary>
/// Names this process in the lease registry. Generated per process rather than per machine, so a
/// restarted replica never claims the leases its predecessor left behind — those have no socket
/// to close and are removed by expiry instead.
/// </summary>
public sealed class RealtimeInstanceIdentity
{
    public RealtimeInstanceIdentity()
        : this($"{Environment.MachineName}:{Guid.NewGuid():n}")
    {
    }

    public RealtimeInstanceIdentity(string value) => Value = value;

    public string Value { get; }
}
