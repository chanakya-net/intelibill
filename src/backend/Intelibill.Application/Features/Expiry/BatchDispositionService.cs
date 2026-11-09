using System.Data.Common;
using ErrorOr;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.Expiry;

public sealed class BatchDispositionService(IBatchDispositionRepository journal, IExpiryPolicyRepository policies,
    IExpiryPolicyFence fence, IShopRepository shops, IInventoryBatchRepository batches, IInventoryRepository inventory,
    IStockTransactionRepository movements, IInventoryAdjustmentRepository adjustments, IInventoryAdjustmentNumberGenerator numbers,
    IFinancialOperationExecutor executor, IPostingPeriodPolicy periods, IBusinessClock clock, IUnitOfWork uow)
{
    private static Error Refused(string code, string message) => Error.Conflict("Expiry." + code, message);
    public static RestorationDisposition EvaluateRestoration(ExpiryBatchFacts batch, ExpiryPolicy policy, DateOnly date, decimal quantity)
    {
        var expired = batch.ExpiryDate.HasValue && (policy.SellThroughExpiryDate ? date > batch.ExpiryDate : date >= batch.ExpiryDate);
        var reason = expired ? "Expiry.Expired" : policy.ExpiryRequired && !batch.ExpiryDate.HasValue ? "Expiry.RequiredDateMissing" : null;
        return new(quantity, reason is null ? 0m : quantity, reason);
    }
    public async Task LockBatchesAsync(Guid shopId, IEnumerable<Guid> batchIds, CancellationToken ct)
    {
        foreach (var id in batchIds.Distinct().Order()) await journal.LockBatchAsync(shopId, id, ct);
    }
    public async Task<ErrorOr<RestorationDisposition>> EvaluateAsync(InventoryBatch batch, DateOnly date, decimal quantity, CancellationToken ct)
    {
        var policy = await policies.GetEffectiveAsync(batch.ShopId, batch.ItemId, null, ct);
        if (!policy.HistoryKnown || policy.Policy is null) return Refused("HistoryUnknown", "Retained expiry policy is unavailable.");
        return EvaluateRestoration(Facts(batch), policy.Policy, date, quantity);
    }
    private static ExpiryBatchFacts Facts(InventoryBatch batch) => new(batch.Id, batch.ItemId, batch.ExpiryDate, batch.ManufacturingDate,
        batch.Quantity, batch.QuarantinedQuantity, batch.IsVoided, batch.Item?.IsActive ?? true, batch.Version);

    public Task<ErrorOr<Guid?>> StageRestorationAsync(Guid shopId, Guid actor, InventoryBatch batch, string sourceKind,
        Guid sourceId, Guid sourceLineId, Guid stockMovementId, RestorationDisposition disposition, DateOnly businessDate, CancellationToken ct) =>
        StageRestorationAsync(shopId, actor, batch, sourceKind, sourceId, sourceLineId, stockMovementId, disposition, businessDate, null, ct);

    public async Task<ErrorOr<Guid?>> StageRestorationAsync(Guid shopId, Guid actor, InventoryBatch batch, string sourceKind,
        Guid sourceId, Guid sourceLineId, Guid stockMovementId, RestorationDisposition disposition, DateOnly businessDate, DateTimeOffset? recordedAt, CancellationToken ct)
    {
        // A clean restoration retains the source writer's existing optimistic xmin boundary.
        // Only a quarantine mutation needs the root/batch serialization lock.
        var locked = disposition.QuarantinedQuantity > 0 ? await journal.LockBatchAsync(shopId, batch.Id, ct) : batch;
        if (locked is null || locked.ShopId != shopId || locked.IsVoided) return Refused("RestorationInvalid", "Original batch is unavailable.");
        var effective = await policies.GetEffectiveAsync(shopId, batch.ItemId, null, ct);
        if (!effective.HistoryKnown || effective.Policy is null) return Refused("HistoryUnknown", "Retained expiry policy is unavailable.");
        // Recompute after the batch lock; preview facts never authorize a different disposition.
        var actual = EvaluateRestoration(Facts(locked), effective.Policy, businessDate, disposition.PhysicalQuantity);
        if (actual != disposition) return Refused("DispositionChanged", "Reload the restoration disposition.");
        var changed = actual.QuarantinedQuantity > 0
            ? locked.RestoreQuarantine(actual.PhysicalQuantity, actor) : locked.AddQuantity(actual.PhysicalQuantity, actor);
        if (changed.IsError) return changed.Errors;
        batches.Update(locked);
        if (actual.QuarantinedQuantity == 0) return (Guid?)null;
        var root = BatchDispositionChange.Quarantine(locked, sourceKind, sourceId, sourceLineId, stockMovementId,
            actual.QuarantinedQuantity, actual.ReasonCode!, effective.Policy, businessDate, actor, recordedAt ?? clock.UtcNow);
        await journal.AddAsync(root, ct);
        return (Guid?)root.Id;
    }
    public Task<ErrorOr<Success>> StageReversalAsync(Guid shopId, Guid actor, InventoryBatch batch, Guid rootChangeId,
        decimal quantity, Guid reversalMovementId, DateOnly businessDate, CancellationToken ct) =>
        StageReversalAsync(shopId, actor, batch, rootChangeId, quantity, reversalMovementId, businessDate, null, null, null, ct);

    public async Task<ErrorOr<Success>> StageReversalAsync(Guid shopId, Guid actor, InventoryBatch batch, Guid rootChangeId,
        decimal quantity, Guid reversalMovementId, DateOnly businessDate, Guid? sourceId, Guid? sourceLineId, DateTimeOffset? recordedAt, CancellationToken ct)
    {
        var locked = await journal.LockBatchAsync(shopId, batch.Id, ct);
        var root = await journal.GetRootAsync(shopId, batch.Id, rootChangeId, ct);
        if (locked is null || root is null || root.SourceKind != "SaleReturn" || root.Quantity != quantity ||
            (sourceId.HasValue && root.SourceId != sourceId) || (sourceLineId.HasValue && root.SourceLineId != sourceLineId))
            return Refused("RootMismatch", "The original return quarantine source is unavailable.");
        var balance = await journal.GetRemainingAsync(shopId, rootChangeId, ct);
        if (balance != root.Quantity) return Refused("SourceDispositionConsumed", "The original quarantine was released or disposed; its return cannot be voided.");
        var changed = locked.DisposeQuarantine(quantity, actor); if (changed.IsError) return changed.Errors;
        batches.Update(locked);
        await journal.AddAsync(BatchDispositionChange.Consume(root, "Reverse", quantity, "Original return void", reversalMovementId, null,
            businessDate, actor, recordedAt ?? clock.UtcNow), ct);
        return Result.Success;
    }
    public async Task<ErrorOr<IReadOnlyList<BatchDispositionChange>>> HistoryAsync(Guid shopId, Guid actor, Guid batchId, CancellationToken ct)
    {
        if (await shops.GetActiveRoleAsync(actor, shopId, ct) is null) return Refused("MembershipRequired", "Active membership required.");
        var batch = await batches.GetByIdWithItemAsync(batchId, shopId, ct);
        if (batch is null) return Error.NotFound("Inventory.BatchNotFound", "Batch not found.");
        return ErrorOrFactory.From(await journal.GetHistoryAsync(shopId, batchId, ct));
    }
    public async Task<ErrorOr<BatchDispositionResponse>> PostAsync(Guid shopId, Guid actor, Guid batchId, BatchDispositionRequest request, CancellationToken ct)
    {
        if (await shops.GetActiveRoleAsync(actor, shopId, ct) != ShopRole.Owner) return Error.Forbidden("Expiry.OwnerRequired", "Only a current owner may dispose or release quarantine.");
        if (string.IsNullOrWhiteSpace(request.OperationKey) || request.OperationKey.Trim().Length > 128 || request.ExpectedVersion == 0 ||
            request.QuarantineChangeId == Guid.Empty || request.Action is not ("Release" or "Dispose") || request.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Error.Validation("Expiry.DispositionInvalid", "Supply an operation key, version, root, positive quantity, action and 1 to 500 character reason.");
        var hash = FinancialOperationHasher.Compute(new { batchId, request.ExpectedVersion, request.QuarantineChangeId, request.Action, request.Quantity, Reason = request.Reason.Trim() });
        try
        {
            return await executor.ExecuteAsync<BatchDispositionResponse>(new(shopId, FinancialOperationType.BatchDisposition, request.OperationKey.Trim()), actor, batchId, hash, async token =>
            {
                if (await shops.GetActiveRoleAsync(actor, shopId, token) != ShopRole.Owner) return Error.Forbidden("Expiry.OwnerRequired", "Current owner required.");
                var shop = await shops.GetByIdAsync(shopId, token); if (shop is null) return Error.NotFound("Shop.NotFound", "Shop not found.");
                var now = clock.UtcNow;
                var day = clock.GetBusinessDate(now, shop.BusinessTimeZoneId);
                var period = await periods.AuthorizeForWriteAsync(shopId, day, token); if (period.IsError) return period.Errors;
                var acquired = await fence.AcquireAsync(shopId, false, token); if (acquired.IsError) return acquired.Errors;
                var batch = await journal.LockBatchAsync(shopId, batchId, token);
                if (batch is null || batch.IsVoided) return Error.NotFound("Inventory.BatchNotFound", "Batch not found.");
                if (batch.Version != request.ExpectedVersion) return Refused("BatchVersionConflict", "Reload the batch version.");
                var detailed = await batches.GetByIdWithItemAsync(batchId, shopId, token);
                if (!QuantityPolicy.FitsScale(request.Quantity, detailed!.Item.QuantityScale)) return Error.Validation("Expiry.QuantityPrecisionExceeded", "Quantity exceeds the item's scale.");
                var root = await journal.GetRootAsync(shopId, batchId, request.QuarantineChangeId, token);
                if (root is null) return Refused("RootMismatch", "Quarantine root does not belong to this shop and batch.");
                var remaining = await journal.GetRemainingAsync(shopId, root.Id, token);
                if (remaining < request.Quantity) return Refused("RootInsufficient", "The original quarantine source has insufficient balance.");
                Guid? movementId = null; InventoryAdjustment? adjustment = null;
                if (request.Action == "Release")
                {
                    if (root.ReasonCode == "Expiry.Expired") return Refused("OriginallyExpired", "Originally expired recovered goods cannot be released.");
                    var effective = await policies.GetEffectiveAsync(shopId, batch.ItemId, null, token);
                    if (!effective.HistoryKnown || effective.Policy is null) return Refused("HistoryUnknown", "Retained policy unavailable.");
                    var clean = EvaluateRestoration(Facts(batch), effective.Policy, day, request.Quantity);
                    var eligible = ExpiryPolicyEvaluator.Evaluate(Facts(batch) with { PhysicalQuantity = request.Quantity, QuarantinedQuantity = 0 }, effective.Policy, effective.CatalogVersion, day);
                    if (batch.ExpiryDate is null || (batch.ManufacturingDate.HasValue && batch.ManufacturingDate > batch.ExpiryDate) || clean.QuarantinedQuantity > 0 || !eligible.Allowed || eligible.RequiresApproval ||
                        !await journal.HasDateCorrectionAsync(shopId, batch.Id, batch.ExpiryDate.Value, root.RecordedAt, token))
                        return Refused("ReleaseIneligible", "A separately audited valid nonexpired date and clean current policy decision are required.");
                    var released = batch.ReleaseQuarantine(request.Quantity, actor); if (released.IsError) return released.Errors;
                }
                else
                {
                    var stock = await inventory.GetByItemAsync(shopId, batch.ItemId, token);
                    if (stock is null) return Refused("InventoryMissing", "Physical inventory aggregate is missing.");
                    var before = batch.Quantity; var aggregate = stock.Quantity;
                    var disposed = batch.DisposeQuarantine(request.Quantity, actor); if (disposed.IsError) return disposed.Errors;
                    var reduced = stock.SubtractQuantity(request.Quantity, actor); if (reduced.IsError) return reduced.Errors;
                    var number = numbers.Generate(day);
                    var movement = StockTransaction.Create(shopId, batch.ItemId, batch.Id, StockTransactionType.Dmg, -request.Quantity, number, request.Reason.Trim(), now, actor, actor);
                    if (movement.IsError) return movement.Errors;
                    movementId = movement.Value.Id; await movements.AddAsync(movement.Value, token);
                    var loss = InventoryAdjustment.Create(shopId, batch.ItemId, batch.Id, number, InventoryAdjustmentDirection.Decrease,
                        root.ReasonCode == "Expiry.Expired" ? InventoryAdjustmentReason.Expired : InventoryAdjustmentReason.OtherLoss, request.Quantity, batch.CostPrice, decimal.Round(request.Quantity * batch.CostPrice, 2, MidpointRounding.AwayFromZero),
                        before, batch.Quantity, aggregate, stock.Quantity, now, actor, request.Reason.Trim(), actor);
                    if (loss.IsError) return loss.Errors;
                    adjustment = loss.Value; inventory.Update(stock);
                }
                var change = BatchDispositionChange.Consume(root, request.Action, request.Quantity, request.Reason.Trim(), movementId, adjustment?.Id, day, actor, now);
                if (adjustment is not null) { adjustment.LinkDisposition(change.Id); await adjustments.AddAsync(adjustment, token); }
                await journal.AddAsync(change, token); batches.Update(batch);
                await uow.SaveChangesAsync(token);
                return new BatchDispositionResponse(change.Id, root.Id, batch.Id, request.Action, request.Quantity, batch.Quantity,
                    batch.QuarantinedQuantity, remaining - request.Quantity, batch.Version, adjustment?.Id);
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { uow.ClearChanges(); return Refused("BatchVersionConflict", "Concurrent stock change; reload."); }
        catch (Exception ex) when (SqlState(ex) is "55P03" or "40P01" or "40001") { uow.ClearChanges(); return Refused("DispositionBusy", "Stock is busy; retry the same operation."); }
    }
    private static string? SqlState(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException) if (current is DbException { SqlState: { } state }) return state;
        return null;
    }
}
