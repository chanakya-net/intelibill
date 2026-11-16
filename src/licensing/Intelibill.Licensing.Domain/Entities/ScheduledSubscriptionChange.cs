namespace Intelibill.Licensing.Domain.Entities;

/// <summary>
/// Operational evidence of the accepted paid-term boundary and frozen shop scope. <see cref="WithdrawnAt"/> is set
/// when the latest revision withdraws it; the change and the original boundary are retained either way.
/// </summary>
public sealed record ScheduledSubscriptionChange(Guid ChangeId, long Version, DateTimeOffset EffectiveAt,
    string PolicyVersion, string ShopHash, int ShopCount, DateTimeOffset? WithdrawnAt = null);
