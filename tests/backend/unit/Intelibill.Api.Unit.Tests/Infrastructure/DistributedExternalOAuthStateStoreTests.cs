using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Services.Auth.ExternalAuth;
using Microsoft.Extensions.Caching.Distributed;

namespace Intelibill.Api.Unit.Tests.Infrastructure;

public class DistributedExternalOAuthStateStoreTests
{
    private readonly RecordingDistributedCache _cache = new();
    private readonly DistributedExternalOAuthStateStore _store;

    public DistributedExternalOAuthStateStoreTests()
    {
        _store = new DistributedExternalOAuthStateStore(_cache);
    }

    [Fact]
    public async Task ConsumeAsync_ReturnsWhatStoreAsyncWrote()
    {
        var value = new ExternalOAuthState(ExternalAuthProvider.Google, "code-verifier");

        await _store.StoreAsync("state-1", value, TimeSpan.FromMinutes(10));
        var consumed = await _store.ConsumeAsync("state-1");

        Assert.Equal(value, consumed);
    }

    [Fact]
    public async Task ConsumeAsync_RoundTripsAStateWithoutAVerifier()
    {
        var value = new ExternalOAuthState(ExternalAuthProvider.Facebook, CodeVerifier: null);

        await _store.StoreAsync("state-2", value, TimeSpan.FromMinutes(10));

        Assert.Equal(value, await _store.ConsumeAsync("state-2"));
    }

    [Fact]
    public async Task ConsumeAsync_IsSingleUse()
    {
        await _store.StoreAsync(
            "state-3",
            new ExternalOAuthState(ExternalAuthProvider.Google, "code-verifier"),
            TimeSpan.FromMinutes(10));

        Assert.NotNull(await _store.ConsumeAsync("state-3"));
        Assert.Null(await _store.ConsumeAsync("state-3"));
    }

    [Fact]
    public async Task ConsumeAsync_ReturnsNullForAnUnknownState()
    {
        Assert.Null(await _store.ConsumeAsync("never-issued"));
    }

    [Fact]
    public async Task StoreAsync_AppliesTheRequestedLifetime()
    {
        await _store.StoreAsync(
            "state-4",
            new ExternalOAuthState(ExternalAuthProvider.Google, null),
            TimeSpan.FromMinutes(10));

        var entry = Assert.Single(_cache.Entries);
        Assert.Equal(TimeSpan.FromMinutes(10), entry.Value.Options.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task StoreAsync_NamespacesTheCacheKey()
    {
        await _store.StoreAsync(
            "state-5",
            new ExternalOAuthState(ExternalAuthProvider.Google, null),
            TimeSpan.FromMinutes(10));

        // The same cache carries rate-limiting state; an unprefixed key would let
        // the two collide.
        Assert.Equal("external-oauth-state:state-5", Assert.Single(_cache.Entries).Key);
    }

    [Fact]
    public async Task StoreAsync_WritesTheProviderByNameNotByOrdinal()
    {
        await _store.StoreAsync(
            "state-6",
            new ExternalOAuthState(ExternalAuthProvider.Facebook, null),
            TimeSpan.FromMinutes(10));

        var payload = System.Text.Encoding.UTF8.GetString(Assert.Single(_cache.Entries).Value.Payload);

        Assert.Contains("Facebook", payload, StringComparison.Ordinal);
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        public Dictionary<string, (byte[] Payload, DistributedCacheEntryOptions Options)> Entries { get; } = [];

        public byte[]? Get(string key) => Entries.TryGetValue(key, out var entry) ? entry.Payload : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => Entries.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Entries[key] = (value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
