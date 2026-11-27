namespace Intelibill.Domain.Entities;

/// <summary>One transaction fence and monotonic invalidation version per shop.</summary>
public sealed class ShopExpiryState
{
    public Guid ShopId { get; private set; }
    public long CatalogVersion { get; private set; }
    public long DefaultPolicyVersion { get; private set; }
    /// <summary>Earliest provable policy instant: creation for a migrated pre-feature shop, initialization for a fresh one.</summary>
    public DateTimeOffset InitializedAt { get; private set; }
    public bool PreFeatureBaseline { get; private set; }
    private ShopExpiryState() { }
    public static ShopExpiryState Create(Guid shopId, DateTimeOffset now) => new()
    {
        ShopId = shopId, CatalogVersion = 1, DefaultPolicyVersion = 1, InitializedAt = now,
    };
    /// <summary>Call only under the exclusive shop expiry fence; date revisions use the same counter.</summary>
    public long Advance(bool defaultPolicyChanged = false)
    {
        CatalogVersion = checked(CatalogVersion + 1);
        if (defaultPolicyChanged) DefaultPolicyVersion = CatalogVersion;
        return CatalogVersion;
    }
}
