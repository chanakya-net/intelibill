using Microsoft.Extensions.Caching.Memory;

namespace Intelibill.Infrastructure.Services.Auth.ExternalAuth;

internal sealed class InMemoryExternalOAuthStateStore(IMemoryCache cache) : IExternalOAuthStateStore
{
    private const string Prefix = "external-oauth-state:";

    public Task StoreAsync(string state, ExternalOAuthState value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        cache.Set(GetCacheKey(state), value, ttl);
        return Task.CompletedTask;
    }

    public Task<ExternalOAuthState?> ConsumeAsync(string state, CancellationToken cancellationToken = default)
    {
        var key = GetCacheKey(state);
        if (!cache.TryGetValue<ExternalOAuthState>(key, out var value))
            return Task.FromResult<ExternalOAuthState?>(null);

        cache.Remove(key);
        return Task.FromResult<ExternalOAuthState?>(value);
    }

    private static string GetCacheKey(string state) => Prefix + state;
}
