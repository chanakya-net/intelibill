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
    public async Task<SaleExpiryEvaluation> BeginEvaluationAsync(Guid shopId, DateOnly businessDate, IEnumerable<Guid> itemIds, CancellationToken ct) =>
        new(shopId, businessDate, await policies.GetEffectiveForItemsAsync(shopId, itemIds.Distinct().ToArray(), ct));

    public async Task<SellabilityDecision> EvaluateBatchAsync(InventoryBatch batch, DateOnly date, CancellationToken ct) =>
        (await BeginEvaluationAsync(batch.ShopId, date, [batch.ItemId], ct)).EvaluateBatch(batch);

    public async Task<IReadOnlyList<SaleExpiryLineDecision>> EvaluateAsync(Guid shopId, DateOnly businessDate, IReadOnlyList<ValidatedSaleLine> lines, CancellationToken ct) =>
        (await BeginEvaluationAsync(shopId, businessDate, lines.Where(x => x.Batch is not null).Select(x => x.Batch!.ItemId), ct)).EvaluateLines(lines);

    public async Task<ErrorOr<IReadOnlyList<SaleExpiryDecision>>> AuthorizeAsync(Guid shopId, Guid actor, DateOnly businessDate,
        IReadOnlyList<ValidatedSaleLine> lines, IReadOnlyList<ExpiryApprovalRequest>? approvals, CancellationToken ct)
    {
        var evaluation = await BeginEvaluationAsync(shopId, businessDate, lines.Where(x => x.Batch is not null).Select(x => x.Batch!.ItemId), ct);
        var decisions = evaluation.EvaluateLines(lines);
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
            var policy = evaluation.PolicyFor(batch.ItemId);
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
