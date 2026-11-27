namespace Intelibill.Domain.ValueObjects;

public enum ExpiryMode { Ignore = 0, Warn = 1, Block = 2 }

public sealed record ExpiryPolicy(long Version, ExpiryMode Mode, bool ExpiryRequired,
    bool SellThroughExpiryDate, int WarningDays, bool OverrideAllowed)
{
    public static ExpiryPolicy Default { get; } = new(1, ExpiryMode.Ignore, false, true, 7, false);
    public bool IsValid() => Version > 0 && Enum.IsDefined(Mode) && WarningDays is >= 0 and <= 365;
}

public sealed record ExpiryBatchFacts(Guid BatchId, Guid ItemId, DateOnly? ExpiryDate,
    DateOnly? ManufacturingDate, decimal PhysicalQuantity, decimal QuarantinedQuantity,
    bool IsVoided, bool ItemActive, uint BatchVersion);

public sealed record SellabilityDecision(bool Allowed, string? ReasonCode, bool Warning,
    bool RequiresApproval, long PolicyVersion, long CatalogVersion, DateOnly BusinessDate,
    decimal PhysicalQuantity, decimal QuarantinedQuantity, decimal AvailableQuantity);

/// <summary>Unknown history carries no policy; callers must never turn it into an Ignore decision.</summary>
public sealed record EffectiveExpiryPolicy(ExpiryPolicy? Policy, long CatalogVersion, bool HistoryKnown,
    bool IsPreFeatureBaseline = false, DateTimeOffset? EffectiveAt = null);

public static class ExpiryPolicyEvaluator
{
    public static SellabilityDecision Evaluate(ExpiryBatchFacts batch, ExpiryPolicy policy, long catalogVersion, DateOnly businessDate)
    {
        var available = batch.PhysicalQuantity - batch.QuarantinedQuantity;
        SellabilityDecision Decision(bool allowed, string? code = null, bool warning = false, bool approval = false) =>
            new(allowed, code, warning, approval, policy.Version, catalogVersion, businessDate,
                batch.PhysicalQuantity, batch.QuarantinedQuantity, allowed ? available : 0m);
        if (!policy.IsValid() || catalogVersion < policy.Version)
            return Decision(false, "Expiry.PolicyInvalid");
        if (!batch.ItemActive) return Decision(false, "Item.Inactive");
        if (batch.IsVoided) return Decision(false, "InventoryBatch.Voided");
        if (batch.QuarantinedQuantity < 0 || batch.PhysicalQuantity < 0 || available <= 0)
            return Decision(false, "InventoryBatch.InsufficientStock");
        if (batch.ExpiryDate is null)
            return policy.ExpiryRequired ? Decision(false, "Expiry.RequiredDateMissing") : Decision(true);
        if (policy.Mode == ExpiryMode.Ignore) return Decision(true);
        var expired = policy.SellThroughExpiryDate ? businessDate > batch.ExpiryDate.Value : businessDate >= batch.ExpiryDate.Value;
        if (expired)
        {
            if (policy.Mode == ExpiryMode.Block) return Decision(false, "Expiry.Expired");
            return policy.OverrideAllowed
                ? Decision(true, "Expiry.Expired", warning: true, approval: true)
                : Decision(false, "Expiry.OverrideDisabled");
        }
        return batch.ExpiryDate.Value.DayNumber - businessDate.DayNumber <= policy.WarningDays
            ? Decision(true, "Expiry.ExpiringSoon", warning: true)
            : Decision(true);
    }
}
