using System.ComponentModel.DataAnnotations;

namespace Intelibill.Api.Options;

/// <summary>
/// Whether, and how far, to believe the <c>X-Forwarded-*</c> headers on an
/// incoming request. Getting this wrong in either direction hurts: unread
/// headers make every client look like the ingress and every request look like
/// plain HTTP, while headers trusted from an untrusted source let a caller
/// choose the client IP the application logs and rate-limits on.
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>
    /// Off by default, because running directly on a developer machine has no
    /// proxy in front of it. Deployed environments sit behind Container Apps
    /// ingress and set this.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// How many proxy hops to unwind. One for a single ingress; raising it means
    /// trusting the hop that added the entry before it.
    /// </summary>
    [Range(1, 8)]
    public int ForwardLimit { get; init; } = 1;

    /// <summary>
    /// Accept forwarded headers from any peer. Container Apps needs this: ingress
    /// reaches the container from an address range that is neither published nor
    /// stable, and nothing else can route to the container. Set it anywhere that
    /// is not true and a caller can forge its own client IP.
    /// </summary>
    public bool TrustAnyProxy { get; init; }

    /// <summary>Individual proxy addresses to trust, when they are known.</summary>
    public IReadOnlyList<string> KnownProxies { get; init; } = [];

    /// <summary>Proxy networks to trust, in CIDR form, when they are known.</summary>
    public IReadOnlyList<string> KnownNetworks { get; init; } = [];
}
