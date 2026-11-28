using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Inventory.Commands.CreateAdjustment;

public sealed class CreateAdjustmentCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IInventoryRepository inventoryRepository,
    IUnitOfWork unitOfWork,
    IItemRepository itemRepository,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    StockCorrectionAccess access,
    StockAdjustmentPoster poster)
{
    private static readonly CreateAdjustmentCommandValidator Validator = new();

    [LicenceAction(LicenceAction.GeneralStock)]
    public async Task<ErrorOr<InventoryAdjustmentResultDto>> HandleAsync(
        CreateAdjustmentCommand command,
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

        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var performedAt = (command.PerformedAt ?? businessClock.UtcNow).ToUniversalTime();
        return await PostingTransaction.RunAsync(
            unitOfWork,
            ct => CreateAsync(command, shop, performedAt, ct),
            cancellationToken);
    }

    private Task<Error?> CheckAuthorityAsync(CreateAdjustmentCommand command, CancellationToken ct) =>
        access.CheckAuthorityAsync(command.ActorUserId, command.ActiveShopId, command.Session, ct);

    private async Task<ErrorOr<InventoryAdjustmentResultDto>> CreateAsync(
        CreateAdjustmentCommand command,
        Shop shop,
        DateTimeOffset performedAt,
        CancellationToken cancellationToken)
    {
        var licence = await access.EnterLicenceAsync(
            command.ActorUserId,
            command.ActiveShopId,
            command.Session,
            retainLease: null,
            cancellationToken);
        if (licence.IsError)
            return licence.Errors;

        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId),
            cancellationToken);
        if (licence.Value.CurrentDecision().EnforcedDenial() is { } periodDenial)
            return periodDenial;
        if (fence.IsError)
            return fence.Errors;

        var batchResult = await LoadAdjustableBatchAsync(command, cancellationToken);
        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;

        var itemCheck = await ValidateQuantityScaleAsync(command, batch, cancellationToken);
        if (itemCheck.IsError)
            return itemCheck.Errors;

        var inventory = await inventoryRepository.GetByItemAsync(command.ActiveShopId, batch.ItemId, cancellationToken);
        if (inventory is null)
            return Errors.Inventory.InventoryAggregateNotFound;

        if (command.Direction == InventoryAdjustmentDirection.Decrease && command.Quantity > batch.UnquarantinedQuantity)
            return Errors.Inventory.AdjustmentQuantityExceedsBatchQuantity;

        var posted = poster.ApplyAdjustment(command, shop, batch, inventory, performedAt);
        if (posted.IsError)
            return posted.Errors;

        var (stockTransaction, adjustment) = posted.Value;

        var saveDenied = await SaveAdjustmentAsync(
            command,
            licence.Value,
            batch,
            inventory,
            stockTransaction,
            adjustment,
            cancellationToken);
        if (saveDenied is { } denied)
            return denied;

        return BuildResult(batch, stockTransaction, adjustment);
    }

    private async Task<ErrorOr<InventoryBatch>> LoadAdjustableBatchAsync(
        CreateAdjustmentCommand command,
        CancellationToken cancellationToken)
    {
        var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
        if (batch is null || batch.ShopId != command.ActiveShopId)
            return Errors.Inventory.BatchNotFound;

        if (batch.IsVoided)
            return Errors.Inventory.BatchAlreadyVoided;

        return batch;
    }

    private async Task<ErrorOr<Success>> ValidateQuantityScaleAsync(
        CreateAdjustmentCommand command,
        InventoryBatch batch,
        CancellationToken cancellationToken)
    {
        var items = await itemRepository.GetByIdsAsync(command.ActiveShopId, [batch.ItemId], cancellationToken);
        var item = items.Count > 0 ? items[0] : null;
        if (item is null)
            return Errors.Inventory.ItemNotFound;

        if (!QuantityPolicy.FitsScale(command.Quantity, item.QuantityScale))
            return Errors.Inventory.QuantityPrecisionExceeded(item.Uom, item.QuantityScale);

        return Result.Success;
    }

    private async Task<Error?> SaveAdjustmentAsync(
        CreateAdjustmentCommand command,
        ShopLicenceLease licence,
        InventoryBatch batch,
        Intelibill.Domain.Entities.Inventory inventory,
        StockTransaction stockTransaction,
        InventoryAdjustment adjustment,
        CancellationToken cancellationToken)
    {
        await poster.StageAdjustmentAsync(batch, inventory, stockTransaction, adjustment, cancellationToken);
        if (await CheckAuthorityAsync(command, cancellationToken) is { } authorityDenied)
            return authorityDenied;
        if (licence.CurrentDecision().EnforcedDenial() is { } saveDenial)
            return saveDenial;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (await CheckAuthorityAsync(command, cancellationToken) is { } postingAuthorityDenied)
            return postingAuthorityDenied;
        if (licence.CurrentDecision().EnforcedDenial() is { } postingDenial)
            return postingDenial;

        return null;
    }

    private static InventoryAdjustmentResultDto BuildResult(
        InventoryBatch batch,
        StockTransaction stockTransaction,
        InventoryAdjustment adjustment) =>
        new(
            adjustment.Id,
            adjustment.AdjustmentNumber,
            batch.Id,
            adjustment.Quantity,
            adjustment.UnitCost,
            adjustment.CostImpact,
            adjustment.BatchQuantityBefore,
            adjustment.BatchQuantityAfter,
            adjustment.InventoryQuantityBefore,
            adjustment.InventoryQuantityAfter,
            stockTransaction.Id,
            adjustment.PerformedAt);
}
