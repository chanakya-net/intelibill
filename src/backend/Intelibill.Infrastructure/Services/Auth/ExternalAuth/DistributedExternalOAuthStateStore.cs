using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;

namespace Intelibill.Infrastructure.Services.Auth.ExternalAuth;

/// <summary>
/// OAuth state has to outlive the replica that issued it. The authorization
/// request leaves from one process and the provider's callback can arrive at
/// another, or at the same one after a restart or a revision swap — an in-memory
/// store answers "state invalid" for every one of those, which the user sees as a
/// login that failed for no reason.
/// </summary>
internal sealed class DistributedExternalOAuthStateStore(IDistributedCache cache) : IExternalOAuthStateStore
{
    private const string KeyPrefix = "external-oauth-state:";

    // Enum names rather than numbers: during a rolling deploy two revisions serve
    // at once, and a renumbered enum would otherwise resolve a state written by
    // one of them to a different provider in the other.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Task StoreAsync(
        string state,
        ExternalOAuthState value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
        => cache.SetAsync(
            KeyPrefix + state,
            JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            cancellationToken);

    /// <summary>
    /// Read then delete. IDistributedCache offers nothing atomic, so two callbacks
    /// racing on the same state value could both be served; both would still need
    /// the same single-use authorization code, which the provider only honours
    /// once, so the exposure ends there.
    /// </summary>
    public async Task<ExternalOAuthState?> ConsumeAsync(string state, CancellationToken cancellationToken = default)
    {
        var key = KeyPrefix + state;

        var payload = await cache.GetAsync(key, cancellationToken);
        if (payload is null)
        {
            return null;
        }

        await cache.RemoveAsync(key, cancellationToken);

        return JsonSerializer.Deserialize<ExternalOAuthState>(payload, SerializerOptions);
    }
}
