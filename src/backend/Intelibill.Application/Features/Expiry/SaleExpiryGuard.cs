using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
namespace Intelibill.Application.Features.Expiry;

/// <summary>Online-only decisions. Posting callers hold the shared expiry fence until commit.</summary>
public sealed class SaleExpiryGuard(IExpiryPolicyRepository policies, IShopRepository shops, IBusinessClock clock)
{
    public async Task<SellabilityDecision> EvaluateBatchAsync(InventoryBatch batch, DateOnly date, CancellationToken ct)
    {
        var policy = await policies.GetEffectiveAsync(batch.ShopId, batch.ItemId, null, ct);
        if (!policy.HistoryKnown || policy.Policy is null)
            return new(false, "Expiry.HistoryUnknown", false, false, 0, policy.CatalogVersion, date, batch.Quantity, batch.QuarantinedQuantity, 0);
        return ExpiryPolicyEvaluator.Evaluate(new(batch.Id, batch.ItemId, batch.ExpiryDate, batch.ManufacturingDate,
            batch.Quantity, batch.QuarantinedQuantity, batch.IsVoided, batch.Item?.IsActive ?? true, batch.Version), policy.Policy, policy.CatalogVersion, date);
    }

    public async Task<IReadOnlyList<SaleExpiryLineDecision>> EvaluateAsync(Guid shopId, DateOnly businessDate, IReadOnlyList<ValidatedSaleLine> lines, CancellationToken ct)
    {
        var result = new List<SaleExpiryLineDecision>();
        foreach (var line in lines.Where(x => x.Batch is not null))
        {
            var batch = line.Batch!;
            var decision = batch.ShopId == shopId ? await EvaluateBatchAsync(batch, businessDate, ct)
                : new SellabilityDecision(false, "Expiry.BatchNotFound", false, false, 0, 0, businessDate, 0, 0, 0);
            result.Add(new(batch.ItemId, batch.Id, line.Command.Quantity, batch.Version, decision));
        }
        return result;
    }

    public async Task<ErrorOr<IReadOnlyList<SaleExpiryDecision>>> AuthorizeAsync(Guid shopId, Guid actor, DateOnly businessDate,
        IReadOnlyList<ValidatedSaleLine> lines, IReadOnlyList<ExpiryApprovalRequest>? approvals, CancellationToken ct)
    {
        var decisions = await EvaluateAsync(shopId, businessDate, lines, ct);
        var blocked = decisions.FirstOrDefault(x => !x.Decision.Allowed);
        if (blocked is not null) return Error.Conflict(blocked.Decision.ReasonCode!, "This lot cannot be sold under its expiry policy.");
        var role = await shops.GetActiveRoleAsync(actor, shopId, ct);
        if (role is null) return Errors.Expiry.MembershipRequired;
        var required = decisions.Where(x => x.Decision.RequiresApproval).GroupBy(x => x.InventoryBatchId).ToDictionary(x => x.Key, x => x.ToList());
        var supplied = new Dictionary<Guid, ExpiryApprovalRequest>();
        foreach (var approval in approvals ?? [])
        {
            if (approval is null || !supplied.TryAdd(approval.InventoryBatchId, approval) || !required.ContainsKey(approval.InventoryBatchId))
                return Error.Validation("Expiry.ApprovalInvalid", "Expiry approvals must name each required lot exactly once.");
        }
        foreach (var (batchId, group) in required)
        {
            if (role is not (ShopRole.Owner or ShopRole.Manager)) return Error.Forbidden("Expiry.ApprovalRoleRequired", "An active owner or manager must approve the warning in their own session.");
            if (!supplied.TryGetValue(batchId, out var approval)) return Error.Conflict("Expiry.ApprovalRequired", "An expiry warning approval is required.");
            if (string.IsNullOrWhiteSpace(approval.Reason) || approval.Reason.Trim().Length > 500) return Errors.Expiry.ReasonRequired;
            var current = group[0];
            if (approval.Quantity != group.Sum(x => x.Quantity) || approval.BatchVersion == 0 || approval.BatchVersion != current.BatchVersion ||
                approval.PolicyVersion != current.Decision.PolicyVersion || approval.CatalogVersion != current.Decision.CatalogVersion)
                return Error.Conflict("Expiry.ApprovalStale", "Lot quantity or expiry facts changed; refresh and approve again.");
        }
        var evidence = new List<SaleExpiryDecision>();
        foreach (var line in lines.Where(x => x.Batch is not null))
        {
            var batch = line.Batch!;
            var policy = await policies.GetEffectiveAsync(shopId, batch.ItemId, null, ct);
            if (!policy.HistoryKnown || policy.Policy is null) return Errors.Expiry.HistoryUnknown;
            var decision = decisions[evidence.Count].Decision;
            supplied.TryGetValue(batch.Id, out var approval);
            evidence.Add(new(Guid.Empty, batch.ItemId, batch.Id, line.Command.Quantity, batch.ExpiryDate, batch.ManufacturingDate,
                policy.Policy, policy.CatalogVersion, batch.Version, businessDate, decision.ReasonCode, decision.Warning,
                approval is null ? null : actor, approval?.Reason.Trim(), approval is null ? null : clock.UtcNow));
        }
        return evidence;
    }
}
