using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Expiry;

/// <summary>Current policy facts for one fenced operation only. Never retained by the guard/service.</summary>
public sealed class SaleExpiryEvaluation(Guid shopId, DateOnly businessDate, IReadOnlyDictionary<Guid, EffectiveExpiryPolicy> policies)
{
    public EffectiveExpiryPolicy PolicyFor(Guid itemId) => policies.GetValueOrDefault(itemId) ?? new(null, 0, false);

    public SellabilityDecision EvaluateBatch(InventoryBatch batch)
    {
        if (batch.ShopId != shopId)
            return new(false, "Expiry.BatchNotFound", false, false, 0, 0, businessDate, 0, 0, 0);
        var policy = PolicyFor(batch.ItemId);
        if (!policy.HistoryKnown || policy.Policy is null)
            return new(false, "Expiry.HistoryUnknown", false, false, 0, policy.CatalogVersion, businessDate, batch.Quantity, batch.QuarantinedQuantity, 0);
        return ExpiryPolicyEvaluator.Evaluate(new(batch.Id, batch.ItemId, batch.ExpiryDate, batch.ManufacturingDate,
            batch.Quantity, batch.QuarantinedQuantity, batch.IsVoided, batch.Item?.IsActive ?? true, batch.Version), policy.Policy, policy.CatalogVersion, businessDate);
    }

    public IReadOnlyList<SaleExpiryLineDecision> EvaluateLines(IReadOnlyList<ValidatedSaleLine> lines) =>
        lines.Where(x => x.Batch is not null).Select(x => new SaleExpiryLineDecision(x.Batch!.ItemId, x.Batch.Id,
            x.Command.Quantity, x.Batch.Version, EvaluateBatch(x.Batch))).ToArray();
}
