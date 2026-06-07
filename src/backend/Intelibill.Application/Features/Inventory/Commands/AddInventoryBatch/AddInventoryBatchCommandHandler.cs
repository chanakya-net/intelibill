using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;

public sealed class AddInventoryBatchCommandHandler(
    IUserRepository userRepository,
    IInboundInventoryLineProcessor inboundInventoryLineProcessor,
    IUnitOfWork unitOfWork)
{
    public AddInventoryBatchCommandHandler(
        IUserRepository userRepository,
        IItemResolver itemResolver,
        IItemRepository itemRepository,
        ISupplierResolver supplierResolver,
        IBatchFactory batchFactory,
        IInventoryUpdater inventoryUpdater,
        IUnitOfWork unitOfWork)
        : this(
            userRepository,
            new InboundInventoryLineProcessor(itemResolver, itemRepository, supplierResolver, batchFactory, inventoryUpdater),
            unitOfWork)
    {
    }

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
        var input = row.ToInboundInput();
        var rowResult = await inboundInventoryLineProcessor.ProcessAsync(
            command.ActiveShopId,
            input,
            command.ActorUserId,
            itemResolutionContext,
            inventoryUpdateContext,
            cancellationToken);

        if (rowResult.IsError)
            return rowResult.Errors;

        return new AddInventoryResultDto(
            rowResult.Value.Item.Id,
            rowResult.Value.Item.Name,
            rowResult.Value.Item.Barcode,
            rowResult.Value.Batch.Id,
            rowResult.Value.Batch.BatchNumber,
            rowResult.Value.Batch.Quantity,
            rowResult.Value.Inventory.Quantity,
            rowResult.Value.Batch.SupplierId,
            rowResult.Value.StockTransaction.Id,
            rowResult.Value.StockTransaction.PerformedAt);
    }

    private static List<Error> ValidateRow(AddInventoryBatchRowCommand row)
    {
        var cmd = new AddInventoryCommand(
            ActorUserId: Guid.Empty,
            ActiveShopId: Guid.Empty,
            row.ItemName,
            row.Barcode,
            row.ItemDescription,
            row.HsnCode,
            row.Uom,
            row.BatchNumber,
            row.Quantity,
            row.TotalPurchaseCost,
            row.Mrp,
            row.SalesPrice,
            row.TaxRatePercent,
            row.TaxIncluded,
            row.PurchaseTaxIncluded,
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
