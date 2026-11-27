using System.Data.Common;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Expiry;

public sealed class BatchDispositionService(IBatchDispositionRepository journal, IExpiryPolicyRepository policies,
    IExpiryPolicyFence fence, IShopRepository shops, IInventoryBatchRepository batches, BatchDisposalPoster disposals,
    IFinancialOperationExecutor executor, IPostingPeriodPolicy periods, IBusinessClock clock, IUnitOfWork uow,
    IShopLicenceFence licenceFence, ILiveSessionAuthority sessions)
{
    private static Error Refused(string code, string message) => Error.Conflict("Expiry." + code, message);
    public static RestorationDisposition EvaluateRestoration(ExpiryBatchFacts batch, ExpiryPolicy policy, DateOnly date, decimal quantity)
    {
        var expired = batch.ExpiryDate.HasValue && (policy.SellThroughExpiryDate ? date > batch.ExpiryDate : date >= batch.ExpiryDate);
        var reason = expired ? "Expiry.Expired" : policy.ExpiryRequired && !batch.ExpiryDate.HasValue ? "Expiry.RequiredDateMissing" : null;
        return new(quantity, reason is null ? 0m : quantity, reason);
    }
    // One call, not one per batch: all item totals are locked before any batch row.
    public Task LockBatchesAsync(Guid shopId, IEnumerable<Guid> batchIds, CancellationToken ct) =>
        journal.LockBatchesAsync(shopId, batchIds.Distinct().ToArray(), ct);
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
    public async Task<ErrorOr<BatchDispositionResponse>> PostAsync(Guid shopId, Guid actor, Guid batchId,
        BatchDispositionRequest request, SessionClaims? session, CancellationToken ct)
    {
        if (await CheckOwnerAsync(shopId, actor, session, ct) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(request.OperationKey) || request.OperationKey.Trim().Length > 128 || request.ExpectedVersion == 0 ||
            request.QuarantineChangeId == Guid.Empty || request.Action is not ("Release" or "Dispose") || request.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Error.Validation("Expiry.DispositionInvalid", "Supply an operation key, version, root, positive quantity, action and 1 to 500 character reason.");

        var posting = new DispositionPosting(shopId, actor, batchId, request, session!);
        var hash = FinancialOperationHasher.Compute(new { batchId, request.ExpectedVersion, request.QuarantineChangeId, request.Action, request.Quantity, Reason = request.Reason.Trim() });
        var scope = new FinancialOperationScope(shopId, FinancialOperationType.BatchDisposition, request.OperationKey.Trim())
        {
            RecheckReplayAuthority = token => CheckOwnerAsync(shopId, actor, session, token),
        };
        try
        {
            return await executor.ExecuteAsync<BatchDispositionResponse>(scope, actor, batchId, hash,
                token => PostUnderFenceAsync(posting, token), ct);
        }
        catch (Exception ex) when (uow.IsConcurrencyConflict(ex)) { uow.ClearChanges(); return Refused("BatchVersionConflict", "Concurrent stock change; reload."); }
        catch (Exception ex) when (SqlState(ex) is "55P03" or "40P01" or "40001") { uow.ClearChanges(); return Refused("DispositionBusy", "Stock is busy; retry the same operation."); }
    }

    private async Task<Error?> CheckOwnerAsync(Guid shopId, Guid actor, SessionClaims? session, CancellationToken ct)
    {
        if (session is null || session.UserId != actor || session.ActiveShopId != shopId || !await sessions.IsValidAsync(session, ct))
            return Errors.Auth.SessionInvalidated;
        return session.ActiveShopRole == nameof(ShopRole.Owner) ? null
            : Error.Forbidden("Expiry.OwnerRequired", "Only a current owner may dispose or release quarantine.");
    }

    private async Task<ErrorOr<BatchDispositionResponse>> PostUnderFenceAsync(DispositionPosting posting, CancellationToken ct)
    {
        var action = posting.Request.Action == "Release" ? LicenceAction.NewTrading : LicenceAction.SafetyDisposition;
        var entered = await licenceFence.EnterAsync(posting.ShopId, action, ct);
        if (entered.IsError) return entered.Errors;
        var lease = entered.Value;
        if (await CheckWriteAuthorityAsync(posting, lease, ct) is { } denied) return denied;

        var shop = await shops.GetByIdAsync(posting.ShopId, ct);
        if (shop is null) return Error.NotFound("Shop.NotFound", "Shop not found.");
        var now = clock.UtcNow;
        var day = clock.GetBusinessDate(now, shop.BusinessTimeZoneId);
        var period = await periods.AuthorizeForWriteAsync(posting.ShopId, day, ct);
        if (lease.CurrentDecision().EnforcedDenial() is { } periodDenial) return periodDenial;
        if (period.IsError) return period.Errors;
        var acquired = await fence.AcquireAsync(posting.ShopId, false, ct);
        if (lease.CurrentDecision().EnforcedDenial() is { } policyDenial) return policyDenial;
        if (acquired.IsError) return acquired.Errors;

        var source = await LockDispositionSourceAsync(posting, lease, ct);
        if (source.IsError) return source.Errors;
        return await SaveDispositionAsync(posting, source.Value, lease, day, now, ct);
    }

    private async Task<ErrorOr<LockedDispositionSource>> LockDispositionSourceAsync(
        DispositionPosting posting, ShopLicenceLease lease, CancellationToken ct)
    {
        var (shopId, _, batchId, request, _) = posting;
        var batch = await journal.LockBatchAsync(shopId, batchId, ct);
        if (await CheckWriteAuthorityAsync(posting, lease, ct) is { } denied) return denied;
        if (batch is null || batch.IsVoided) return Error.NotFound("Inventory.BatchNotFound", "Batch not found.");
        if (batch.Version != request.ExpectedVersion) return Refused("BatchVersionConflict", "Reload the batch version.");
        var detailed = await batches.GetByIdWithItemAsync(batchId, shopId, ct);
        if (!QuantityPolicy.FitsScale(request.Quantity, detailed!.Item.QuantityScale))
            return Error.Validation("Expiry.QuantityPrecisionExceeded", "Quantity exceeds the item's scale.");

        var root = await journal.GetRootAsync(shopId, batchId, request.QuarantineChangeId, ct);
        if (root is null || root.ItemId != batch.ItemId || root.Action != "Quarantine")
            return Refused("RootMismatch", "Quarantine root does not belong to this shop and batch.");
        var remaining = await journal.GetRemainingAsync(shopId, root.Id, ct);
        if (remaining < request.Quantity)
            return Refused("RootInsufficient", "The original quarantine source has insufficient balance.");

        return new LockedDispositionSource(batch, root, remaining);
    }

    private async Task<ErrorOr<BatchDispositionResponse>> SaveDispositionAsync(DispositionPosting posting,
        LockedDispositionSource source, ShopLicenceLease lease, DateOnly day, DateTimeOffset now, CancellationToken ct)
    {
        var (_, actor, _, request, _) = posting;
        var (batch, root, remaining) = source;
        if (CheckSource(lease, root, now) is { } sourceDenial) return sourceDenial;

        InventoryAdjustment? adjustment = null;
        Guid? movementId = null;
        if (request.Action == "Release")
        {
            var released = await ReleaseAsync(posting, source, day, ct);
            if (released.IsError) return released.Errors;
        }
        else
        {
            var disposed = await disposals.StageDisposalAsync(posting.ShopId, actor, request, batch, root, day, now, ct);
            if (disposed.IsError) return disposed.Errors;
            (adjustment, movementId) = disposed.Value;
        }

        var change = BatchDispositionChange.Consume(root, request.Action, request.Quantity, request.Reason.Trim(),
            movementId, adjustment?.Id, day, actor, now);
        if (adjustment is not null)
            await disposals.AddLinkedAdjustmentAsync(adjustment, change.Id, ct);
        await journal.AddAsync(change, ct);
        batches.Update(batch);

        if (await CheckWriteAuthorityAsync(posting, lease, ct) is { } writeDenial) return writeDenial;
        if (CheckSource(lease, root, now) is { } preSaveDenial) return preSaveDenial;
        await uow.SaveChangesAsync(ct);
        if (await CheckWriteAuthorityAsync(posting, lease, ct) is { } savedDenial) return savedDenial;
        if (CheckSource(lease, root, now) is { } postSaveDenial) return postSaveDenial;
        return new BatchDispositionResponse(change.Id, root.Id, batch.Id, request.Action, request.Quantity, batch.Quantity,
            batch.QuarantinedQuantity, remaining - request.Quantity, batch.Version, adjustment?.Id);
    }

    private async Task<Error?> CheckWriteAuthorityAsync(DispositionPosting posting, ShopLicenceLease lease, CancellationToken ct) =>
        await CheckOwnerAsync(posting.ShopId, posting.Actor, posting.Session, ct) ?? lease.CurrentDecision().EnforcedDenial();

    private Error? CheckSource(ShopLicenceLease lease, BatchDispositionChange root, DateTimeOffset performedAt)
    {
        var decision = lease.CurrentDecision();
        if (decision.EnforcedDenial() is { } denied) return denied;
        return decision is { Enforced: true, Outcome: LicenceOutcome.RequiresProvenSource } &&
            (root.RecordedAt == default || root.RecordedAt > performedAt || root.RecordedAt > clock.UtcNow)
            ? Refused("RootIneligible", "An existing quarantine source is required.") : null;
    }

    private async Task<ErrorOr<Success>> ReleaseAsync(DispositionPosting posting, LockedDispositionSource source,
        DateOnly day, CancellationToken ct)
    {
        var (shopId, actor, _, request, _) = posting;
        var (batch, root, _) = source;
        if (root.ReasonCode == "Expiry.Expired")
            return Refused("OriginallyExpired", "Originally expired recovered goods cannot be released.");
        var effective = await policies.GetEffectiveAsync(shopId, batch.ItemId, null, ct);
        if (!effective.HistoryKnown || effective.Policy is null) return Refused("HistoryUnknown", "Retained policy unavailable.");
        var clean = EvaluateRestoration(Facts(batch), effective.Policy, day, request.Quantity);
        var eligible = ExpiryPolicyEvaluator.Evaluate(Facts(batch) with { PhysicalQuantity = request.Quantity, QuarantinedQuantity = 0 },
            effective.Policy, effective.CatalogVersion, day);
        if (batch.ExpiryDate is null || batch.ManufacturingDate > batch.ExpiryDate || clean.QuarantinedQuantity > 0 ||
            !eligible.Allowed || eligible.RequiresApproval ||
            !await journal.HasDateCorrectionAsync(shopId, batch.Id, batch.ExpiryDate.Value, root.RecordedAt, ct))
            return Refused("ReleaseIneligible", "A separately audited valid nonexpired date and clean current policy decision are required.");

        return batch.ReleaseQuarantine(request.Quantity, actor);
    }

    private sealed record DispositionPosting(Guid ShopId, Guid Actor, Guid BatchId, BatchDispositionRequest Request, SessionClaims Session);
    private sealed record LockedDispositionSource(InventoryBatch Batch, BatchDispositionChange Root, decimal Remaining);

    private static string? SqlState(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException) if (current is DbException { SqlState: { } state }) return state;
        return null;
    }
}
