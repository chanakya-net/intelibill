using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

/// <summary>Immutable full policy or inheritance event. Version 1 can explicitly prove the pre-feature baseline.</summary>
public sealed class ExpiryPolicyRevision
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ShopId { get; private set; }
    public Guid? ItemId { get; private set; }
    public long CatalogVersion { get; private set; }
    public ExpiryMode? Mode { get; private set; }
    public bool? ExpiryRequired { get; private set; }
    public bool? SellThroughExpiryDate { get; private set; }
    public int? WarningDays { get; private set; }
    public bool? OverrideAllowed { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset EffectiveAt { get; private set; }
    public bool PreFeatureBaseline { get; private set; }
    public ExpiryPolicy? Policy => Mode.HasValue && ExpiryRequired.HasValue && SellThroughExpiryDate.HasValue && WarningDays.HasValue && OverrideAllowed.HasValue
        ? new(CatalogVersion, Mode.Value, ExpiryRequired.Value, SellThroughExpiryDate.Value, WarningDays.Value, OverrideAllowed.Value) : null;
    private ExpiryPolicyRevision() { }
    public static ExpiryPolicyRevision Create(Guid shopId, Guid? itemId, long version, ExpiryPolicy? policy, Guid actor, string reason, DateTimeOffset now)
    {
        if (version <= 0 || (policy is null && itemId is null) || (policy is not null && !policy.IsValid()))
            throw new ArgumentException("A valid policy or item inheritance event is required.", nameof(policy));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("A reason of 1 to 500 characters is required.", nameof(reason));
        return new()
        {
            ShopId = shopId, ItemId = itemId, CatalogVersion = version, Mode = policy?.Mode,
            ExpiryRequired = policy?.ExpiryRequired, SellThroughExpiryDate = policy?.SellThroughExpiryDate,
            WarningDays = policy?.WarningDays, OverrideAllowed = policy?.OverrideAllowed,
            ActorUserId = actor, Reason = reason.Trim(), EffectiveAt = now,
        };
    }
}
