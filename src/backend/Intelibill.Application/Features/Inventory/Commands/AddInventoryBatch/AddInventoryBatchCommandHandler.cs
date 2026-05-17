using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;

public sealed class AddInventoryBatchCommandHandler(
    IUserRepository userRepository,
    IItemResolver itemResolver,
    IItemRepository itemRepository,
    ISupplierResolver supplierResolver,
    IBatchFactory batchFactory,
    IInventoryUpdater inventoryUpdater,
    IUnitOfWork unitOfWork)
{
    private const int MaxBatchSize = 100;
    private static readonly AddInventoryCommandValidator RowValidator = new();

    public async Task<ErrorOr<AddInventoryBatchResultDto>> HandleAsync(AddInventoryBatchCommand command, CancellationToken cancellationToken)
    {
        if (command.Items.Count == 0)
            return Error.Validation("Inventory.BatchEmpty", "At least one inventory row is required.");

        if (command.Items.Count > MaxBatchSize)
            return Error.Validation("Inventory.BatchLimitExceeded", "Only 100 items are allowed in a batch.");

        var auth = await AuthorizeAsync(command, cancellationToken);
        if (auth.IsError)
            return auth.Errors;

        var succeeded = new List<AddInventoryBatchSucceededRowDto>();
        var failed = new List<AddInventoryBatchFailedRowDto>();
        var itemResolutionContext = new ItemResolutionContext();
        var inventoryUpdateContext = new InventoryUpdateContext();

        foreach (var row in command.Items)
        {
            var rowErrors = ValidateRow(row);
            if (rowErrors.Count > 0)
            {
                failed.Add(ToFailedRow(row, rowErrors));
                continue;
            }

            var rowResult = await ProcessRowAsync(command, row, itemResolutionContext, inventoryUpdateContext, cancellationToken);

            if (rowResult.IsError)
            {
                failed.Add(ToFailedRow(row, rowResult.Errors));
                continue;
            }

            succeeded.Add(new AddInventoryBatchSucceededRowDto(row.ClientRowId, rowResult.Value));
        }

        if (succeeded.Count > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AddInventoryBatchResultDto(
            command.Items.Count,
            succeeded.Count,
            failed.Count,
            succeeded,
            failed);
    }

    private async Task<ErrorOr<Success>> AuthorizeAsync(AddInventoryBatchCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Inventory.UserIsNotOwnerOrManager;

        return Result.Success;
    }

    private async Task<ErrorOr<AddInventoryResultDto>> ProcessRowAsync(
        AddInventoryBatchCommand command,
        AddInventoryBatchRowCommand row,
        ItemResolutionContext itemResolutionContext,
        InventoryUpdateContext inventoryUpdateContext,
        CancellationToken cancellationToken)
    {
        var itemOrError = await itemResolver.ResolveAsync(
            command.ActiveShopId,
            row.ItemName,
            row.Barcode,
            row.ItemDescription,
            row.Uom,
            command.ActorUserId,
            itemResolutionContext,
            cancellationToken);

        if (itemOrError.IsError)
            return itemOrError.Errors;

        var item = itemOrError.Value;

        if (!string.IsNullOrWhiteSpace(row.HsnCode))
        {
            item.UpdateHsnCode(row.HsnCode);

            // Avoid switching a newly-added item (Added) to Modified; that would prevent INSERT and break FK integrity.
            var existingItem = await itemRepository.GetByBarcodeAsync(command.ActiveShopId, row.Barcode.Trim(), cancellationToken);
            if (existingItem is not null)
                itemRepository.Update(item);
        }

        var supplierOrError = await supplierResolver.ResolveAsync(
            command.ActiveShopId,
            row.SupplierId,
            cancellationToken);

        if (supplierOrError.IsError)
            return supplierOrError.Errors;

        var supplier = supplierOrError.Value;

        var batchOrError = await batchFactory.CreateBatchAsync(
            command.ActiveShopId,
            item.Id,
            row,
            supplier,
            command.ActorUserId,
            cancellationToken);

        if (batchOrError.IsError)
            return batchOrError.Errors;

        var (batch, stockTransaction, _) = batchOrError.Value;

        var inventoryOrError = await inventoryUpdater.GetOrUpdateAsync(
            command.ActiveShopId,
            item.Id,
            row.Quantity,
            command.ActorUserId,
            inventoryUpdateContext,
            cancellationToken);

        if (inventoryOrError.IsError)
            return inventoryOrError.Errors;

        var inventory = inventoryOrError.Value;

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

    private static List<Error> ValidateRow(AddInventoryBatchRowCommand row)
    {
        var cmd = new AddInventoryCommand(
            ActorUserId: Guid.Empty,
            ActiveShopId: Guid.Empty,
            row.ItemName,
            row.Barcode,
            row.ItemDescription,
            row.Uom,
            row.BatchNumber,
            row.Quantity,
            row.CostPrice,
            row.Mrp,
            row.SalesPrice,
            row.TaxRatePercent,
            row.TaxIncluded,
            row.ExpiryDate,
            row.ManufacturingDate,
            row.SupplierId,
            row.ReferenceNumber,
            row.Notes,
            row.PerformedAt);

        var result = RowValidator.Validate(cmd);
        return result.Errors
            .Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage))
            .ToList();
    }

    private static AddInventoryBatchFailedRowDto ToFailedRow(AddInventoryBatchRowCommand row, IReadOnlyList<Error> errors) =>
        new(
            row.ClientRowId,
            row.ItemName,
            row.Barcode,
            errors.Select(error => new AddInventoryBatchRowErrorDto(error.Code, error.Description)).ToArray());
}
