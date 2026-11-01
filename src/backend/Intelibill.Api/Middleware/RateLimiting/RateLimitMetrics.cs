using System.Diagnostics.Metrics;

namespace Intelibill.Api.Middleware.RateLimiting;

/// <summary>
/// Rate-limit decisions and store outages on the application meter. Tags are a closed set —
/// outcome, mode — and never name a route, an address, an account or a destination.
/// </summary>
public sealed class RateLimitMetrics
{
    public const string DecisionsInstrumentName = "intelibill.rate_limit.decisions";
    public const string StoreUnavailableInstrumentName = "intelibill.rate_limit.store_unavailable";

    private readonly Counter<long> _decisions;
    private readonly Counter<long> _storeUnavailable;

    public RateLimitMetrics(Meter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        _decisions = meter.CreateCounter<long>(
            DecisionsInstrumentName,
            description: "Rate-limit decisions by outcome: allowed, denied, or shadow_denied (a subject budget that would have refused in enforce mode).");
        _storeUnavailable = meter.CreateCounter<long>(
            StoreUnavailableInstrumentName,
            description: "Requests decided without the shared rate-limit store, by mode: fail_closed (refused with 503) or local_fallback.");
    }

    public void Allowed() => _decisions.Add(1, new KeyValuePair<string, object?>("outcome", "allowed"));

    public void Denied() => _decisions.Add(1, new KeyValuePair<string, object?>("outcome", "denied"));

    public void ShadowDenied() => _decisions.Add(1, new KeyValuePair<string, object?>("outcome", "shadow_denied"));

    public void StoreUnavailable(bool failClosed) =>
        _storeUnavailable.Add(1, new KeyValuePair<string, object?>("mode", failClosed ? "fail_closed" : "local_fallback"));
}
