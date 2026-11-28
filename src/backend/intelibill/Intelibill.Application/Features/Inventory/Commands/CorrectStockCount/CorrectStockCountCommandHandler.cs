using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Inventory.Commands.CorrectStockCount;

/// <summary>
/// Applies a physical count to a batch as one atomic, attributable correction.
/// </summary>
/// <remarks>
/// <para>
/// The delta is computed from the quantity observed at <c>ExpectedVersion</c>, never taken from the
/// client. A nonzero delta writes exactly one signed movement (<c>In +d</c> or <c>Out -d</c>) and one
/// <see cref="InventoryAdjustment"/>; every count writes a <see cref="StockCount"/>. All of it commits
/// in one save or not at all.
/// </para>
/// <para>
/// The batch row is always written, even for an equal count, so the save is checked against the
/// version the count was taken at: a count racing any other change of the batch is refused with
/// <c>Inventory.BatchVersionConflict</c> and is never reapplied to the newer quantity.
/// </para>
/// </remarks>
public sealed class CorrectStockCountCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IUnitOfWork unitOfWork,
    IFinancialOperationExecutor operationExecutor,
    IItemRepository itemRepository,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    StockCorrectionAccess access,
    StockCountPoster poster)
{
    private static readonly CorrectStockCountCommandValidator Validator = new();

    [LicenceAction(LicenceAction.GeneralStock)]
    public async Task<ErrorOr<StockCountResultDto>> HandleAsync(
        CorrectStockCountCommand command,
        CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (!validation.IsValid)
        {
            return validation.Errors
                .Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage))
                .ToList();
        }

        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null || !actorMembership.IsActive)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Inventory.UserIsNotOwnerOrManager;

        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        if (operationKey is null)
            return await PostingTransaction.RunAsync(unitOfWork, ct => CountAsync(command, null, ct), cancellationToken);

        var requestHash = FinancialOperationHasher.Compute(new StockCountPayload(
            command.BatchId,
            command.ExpectedVersion,
            FinancialOperationHasher.NormalizeAmount(command.CountedQuantity),
            command.Reason.Trim()));

        // The receipt is completed after the count is saved. The count's own lease is read once more
        // after that write, while the executor can still roll the whole posting back.
        ShopLicenceLease? posting = null;
        return await operationExecutor.ExecuteAsync(
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.StockCount, operationKey)
            {
                RecheckReplayAuthority = ct => CheckAuthorityAsync(command, ct),
                RecheckBeforeCommit = async ct =>
                    await CheckAuthorityAsync(command, ct) ?? posting?.CurrentDecision().EnforcedDenial(),
            },
            command.ActorUserId,
            command.BatchId,
            requestHash,
            ct => CountAsync(command, lease => posting = lease, ct),
            cancellationToken);
    }

    private Task<Error?> CheckAuthorityAsync(CorrectStockCountCommand command, CancellationToken ct) =>
        access.CheckAuthorityAsync(command.ActorUserId, command.ActiveShopId, command.Session, ct);

    private async Task<ErrorOr<StockCountResultDto>> CountAsync(
        CorrectStockCountCommand command,
        Action<ShopLicenceLease>? retainLease,
        CancellationToken cancellationToken)
    {
        var licence = await access.EnterLicenceAsync(
            command.ActorUserId,
            command.ActiveShopId,
            command.Session,
            retainLease,
            cancellationToken);
        if (licence.IsError)
            return licence.Errors;

        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            businessClock.Today(shop.BusinessTimeZoneId),
            cancellationToken);
        if (licence.Value.CurrentDecision().EnforcedDenial() is { } periodDenial)
            return periodDenial;
        if (fence.IsError)
            return fence.Errors;

        if (CheckBatchCountable(command, batch) is { } batchDenied)
            return batchDenied;

        var scaleCheck = await ValidateQuantityScaleAsync(command, batch, cancellationToken);
        if (scaleCheck.IsError)
            return scaleCheck.Errors;

        var inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, batch.ItemId, cancellationToken);
        if (inventory is null)
            return Errors.Inventory.InventoryAggregateNotFound;

        var performedAt = businessClock.UtcNow;
        var businessDate = businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId);
        var observedQuantity = batch.Quantity;
        var delta = command.CountedQuantity - observedQuantity;

        InventoryAdjustment? adjustment = null;
        StockTransaction? movement = null;
        if (delta != 0)
        {
            var posted = poster.PostDelta(batch, inventory, command, delta, performedAt, businessDate);
            if (posted.IsError)
                return posted.Errors;

            (adjustment, movement) = posted.Value;
        }

        var count = StockCount.Create(
            command.ActiveShopId,
            batch.ItemId,
            batch.Id,
            command.ExpectedVersion,
            observedQuantity,
            command.CountedQuantity,
            command.Reason,
            performedAt,
            command.ActorUserId,
            adjustment?.Id,
            movement?.Id);

        if (count.IsError)
            return count.Errors;

        var saveDenied = await SaveCountAsync(
            command,
            licence.Value,
            batch,
            inventory,
            adjustment,
            movement,
            count.Value,
            cancellationToken);
        if (saveDenied is { } denied)
            return denied;

        return new StockCountResultDto(
            count.Value.Id,
            batch.Id,
            observedQuantity,
            command.CountedQuantity,
            delta,
            adjustment?.Id,
            adjustment?.AdjustmentNumber,
            movement?.Id,
            batch.Quantity,
            inventory.Quantity,
            batch.Version,
            performedAt);
    }

    private static Error? CheckBatchCountable(CorrectStockCountCommand command, InventoryBatch batch)
    {
        if (command.CountedQuantity < batch.QuarantinedQuantity)
            return Error.Conflict("Expiry.QuarantineProtected", "Count cannot reduce physical stock below quarantine.");

        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        if (batch.Version != command.ExpectedVersion)
            return Errors.Inventory.BatchVersionConflict;

        return null;
    }

    private async Task<ErrorOr<Success>> ValidateQuantityScaleAsync(
        CorrectStockCountCommand command,
        InventoryBatch batch,
        CancellationToken cancellationToken)
    {
        var items = await itemRepository.GetByIdsAsync(command.ActiveShopId, [batch.ItemId], cancellationToken);
        var item = items.Count > 0 ? items[0] : null;
        if (item is null)
            return Errors.Inventory.ItemNotFound;

        if (!QuantityPolicy.FitsScale(command.CountedQuantity, item.QuantityScale))
            return Errors.Inventory.QuantityPrecisionExceeded(item.Uom, item.QuantityScale);

        return Result.Success;
    }

    private async Task<Error?> SaveCountAsync(
        CorrectStockCountCommand command,
        ShopLicenceLease licence,
        InventoryBatch batch,
        Domain.Entities.Inventory inventory,
        InventoryAdjustment? adjustment,
        StockTransaction? movement,
        StockCount count,
        CancellationToken cancellationToken)
    {
        await poster.StageCountAsync(command, batch, inventory, adjustment, movement, count, cancellationToken);

        if (await CheckAuthorityAsync(command, cancellationToken) is { } authorityDenied)
            return authorityDenied;
        if (licence.CurrentDecision().EnforcedDenial() is { } saveDenial)
            return saveDenial;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (unitOfWork.IsConcurrencyConflict(ex))
        {
            return Errors.Inventory.BatchVersionConflict;
        }

        if (await CheckAuthorityAsync(command, cancellationToken) is { } postingAuthorityDenied)
            return postingAuthorityDenied;
        if (licence.CurrentDecision().EnforcedDenial() is { } postingDenial)
            return postingDenial;

        return null;
    }

    private sealed record StockCountPayload(Guid BatchId, uint ExpectedVersion, decimal CountedQuantity, string Reason);
}
