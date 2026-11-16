namespace Intelibill.Licensing.Domain.Entities;

/// <summary>Operational evidence of the accepted paid-term boundary and frozen shop scope.</summary>
public sealed record ScheduledSubscriptionChange(Guid ChangeId, long Version, DateTimeOffset EffectiveAt,
    string PolicyVersion, string ShopHash, int ShopCount);
