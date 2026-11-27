using Intelibill.Application.Common.Interfaces;

namespace Intelibill.Api.Middleware.RateLimiting;

/// <summary>
/// What a fail-open endpoint is limited by while the shared store is unreachable: fixed windows
/// held in this process only. It is per replica, so it bounds what one replica serves, not what
/// the service serves, and the filter never presents its numbers as the shared limit.
/// <para>
/// Memory is bounded: past <see cref="Capacity"/> windows, ended ones are dropped, and if every
/// window is still live a bucket it has not seen is refused rather than admitted unlimited.
/// </para>
/// </summary>
public sealed class LocalFallbackRateLimiter
{
    public const int Capacity = 10_000;

    private readonly Dictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public RateDecision Acquire(RateBudget budget, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var id = $"{budget.PolicyId}\n{budget.Key}";

        lock (_gate)
        {
            if (!_windows.TryGetValue(id, out var window) || window.End <= now)
            {
                if (!_windows.ContainsKey(id) && _windows.Count >= Capacity)
                {
                    Purge(now);
                    if (_windows.Count >= Capacity)
                    {
                        var retryAt = now.AddSeconds(budget.WindowSeconds);
                        return new RateDecision(false, budget.Limit, 0, retryAt, retryAt);
                    }
                }

                window = new Window(now.AddSeconds(budget.WindowSeconds), 0);
            }

            if (window.Count >= budget.Limit)
            {
                _windows[id] = window;
                return new RateDecision(false, budget.Limit, 0, window.End, window.End);
            }

            window = window with { Count = window.Count + 1 };
            _windows[id] = window;
            return new RateDecision(true, budget.Limit, budget.Limit - window.Count, window.End, null);
        }
    }

    private void Purge(DateTimeOffset now)
    {
        foreach (var (id, window) in _windows.ToArray())
        {
            if (window.End <= now)
                _windows.Remove(id);
        }
    }

    private readonly record struct Window(DateTimeOffset End, int Count);
}
