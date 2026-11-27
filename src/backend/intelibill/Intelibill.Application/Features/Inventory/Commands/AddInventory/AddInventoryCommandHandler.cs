using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Expiry;
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
using DomainInventory = Intelibill.Domain.Entities.Inventory;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventory;

public sealed class AddInventoryCommandHandler(
    IUserRepository userRepository,
    IInventoryRepository inventoryRepository,
    IUnitOfWork unitOfWork,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock, IExpiryPolicyFence expiryFence,
    StockIntakeAccess access,
    ReceiptIntakeResolver intake,
    ReceiptStockAndPayablePoster poster)
{
    [LicenceAction(LicenceAction.NewTrading)]
    public async Task<ErrorOr<AddInventoryResultDto>> HandleAsync(AddInventoryCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Inventory.UserIsNotOwnerOrManager;

        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var performedAt = (command.PerformedAt ?? businessClock.UtcNow).ToUniversalTime();
        return await PostingTransaction.RunAsync(
            unitOfWork,
            ct => PostAsync(command, shop, performedAt, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<AddInventoryResultDto>> PostAsync(
        AddInventoryCommand command,
        Shop shop,
        DateTimeOffset performedAt,
        CancellationToken cancellationToken)
    {
        var licence = await access.EnterAsync(command.ActorUserId, command.ActiveShopId, command.Session, cancellationToken);
        if (licence.IsError)
            return licence.Errors;

        // The payable is dated by the business day of the receipt's performed time.
        var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
            command.ActiveShopId,
            businessClock.GetBusinessDate(performedAt, shop.BusinessTimeZoneId),
            cancellationToken);
        if (fence.IsError)
            return fence.Errors;

        var expiryLock = await expiryFence.AcquireAsync(command.ActiveShopId, false, cancellationToken);
        if (expiryLock.IsError) return expiryLock.Errors;
        if (licence.Value.CurrentDecision().EnforcedDenial() is { } denied)
            return denied;

        return await StageReceiptAsync(command, shop, performedAt, licence.Value, cancellationToken);
    }

    private async Task<ErrorOr<AddInventoryResultDto>> StageReceiptAsync(
        AddInventoryCommand command,
        Shop shop,
        DateTimeOffset performedAt,
        ShopLicenceLease licence,
        CancellationToken cancellationToken)
    {
        var resolved = await intake.ResolveItemAsync(command, cancellationToken);
        if (resolved.IsError)
            return resolved.Errors;

        var item = resolved.Value.Item;

        var prepared = await intake.PrepareItemAsync(command, resolved.Value, cancellationToken);
        if (prepared.IsError)
            return prepared.Errors;

        var batchNumberCheck = await intake.EnsureBatchNumberFreeAsync(command, item, cancellationToken);
        if (batchNumberCheck.IsError)
            return batchNumberCheck.Errors;

        var effectiveSupplierOrError = await intake.ResolveEffectiveSupplierAsync(
            command.SupplierId,
            command.ActiveShopId,
            cancellationToken);
        if (effectiveSupplierOrError.IsError)
            return effectiveSupplierOrError.Errors;

        var effectiveSupplier = effectiveSupplierOrError.Value;

        var batchResult = await poster.StageBatchAsync(command, item, effectiveSupplier, cancellationToken);
        if (batchResult.IsError)
            return batchResult.Errors;

        var batch = batchResult.Value;

        var stockTransactionResult = await poster.StageStockTransactionAsync(
            command,
            item,
            batch,
            performedAt,
            cancellationToken);
        if (stockTransactionResult.IsError)
            return stockTransactionResult.Errors;

        var stockTransaction = stockTransactionResult.Value;

        var ledgerAmount = ComputeLedgerAmount(command.TotalPurchaseCost);

        var receiptValue = await poster.StageReceiptValueAsync(
            command,
            item,
            batch,
            effectiveSupplier,
            ledgerAmount,
            performedAt,
            cancellationToken);
        if (receiptValue.IsError)
            return receiptValue.Errors;

        var ledger = await poster.StageSupplierLedgerEntryAsync(
            command,
            shop,
            batch,
            effectiveSupplier,
            ledgerAmount,
            performedAt,
            cancellationToken);
        if (ledger.IsError)
            return ledger.Errors;

        var inventoryResult = await UpsertInventoryWithRetryAsync(
            command, item.Id, licence, cancellationToken);

        if (inventoryResult.IsError)
            return inventoryResult.Errors;

        var inventory = inventoryResult.Value;

        return new AddInventoryResultDto(
            item.Id,
            item.Name,
            item.Barcode,
            batch.Id,
            batch.BatchNumber,
            batch.Quantity,
            inventory.Quantity,
            batch.SupplierId,
            stockTransaction.Id,
            stockTransaction.PerformedAt);
    }

    private async Task<ErrorOr<DomainInventory>> UpsertInventoryWithRetryAsync(
        AddInventoryCommand command,
        Guid itemId,
        ShopLicenceLease licence,
        CancellationToken ct,
        int maxRetries = 3)
    {
        var (shopId, createdBy, quantityToAdd) = (command.ActiveShopId, command.ActorUserId, command.Quantity);
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var inventory = await inventoryRepository.GetByItemAsync(shopId, itemId, ct);

                if (inventory is null)
                {
                    var createResult = DomainInventory.Create(shopId, itemId, quantityToAdd, reorderLevel: 0, maxLevel: 0, createdBy);
                    if (createResult.IsError)
                        return createResult.Errors;

                    inventory = createResult.Value;
                    await inventoryRepository.AddAsync(inventory, ct);
                }
                else
                {
                    var addResult = inventory.AddQuantity(quantityToAdd, createdBy);
                    if (addResult.IsError)
                        return addResult.Errors;

                    inventoryRepository.Update(inventory);
                }

                if (await access.CheckWriteAsync(createdBy, shopId, command.Session, licence, ct) is { } beforeSave)
                    return beforeSave;

                await unitOfWork.SaveChangesAsync(ct);
                if (await access.CheckWriteAsync(createdBy, shopId, command.Session, licence, ct) is { } beforeCommit)
                    return beforeCommit;

                return inventory;
            }
            catch (Exception ex) when (unitOfWork.IsConcurrencyConflict(ex))
            {
                if (attempt == maxRetries - 1)
                    return Error.Conflict(
                        "Inventory.UpdateConflict",
                        "Inventory aggregate could not be updated after 3 retries due to concurrent modifications.");

                await unitOfWork.ReloadConflictedEntitiesAsync(ex, ct);
            }
        }

        return Error.Conflict(
            "Inventory.UpdateConflict",
            "Inventory aggregate could not be updated after max retries.");
    }

    private static decimal ComputeLedgerAmount(decimal totalPurchaseCost) =>
        decimal.Round(totalPurchaseCost, 2, MidpointRounding.AwayFromZero);
}
