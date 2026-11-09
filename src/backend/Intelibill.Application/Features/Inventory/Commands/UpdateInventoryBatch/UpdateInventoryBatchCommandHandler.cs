using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Common.Posting;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Events;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;

/// <summary>
/// Legacy whole-batch edit, kept for clients that still send it. It no longer voids and replaces
/// the batch: it applies retail prices and lot details in place, like <c>PUT batches/{id}/prices</c>.
/// </summary>
/// <remarks>
/// The old replacement rewrote the receipt payable from the remaining quantity and orphaned every
/// sale made from the batch, so the parts of the request that would change stock or money are
/// refused with a code naming the action that does it now: a quantity change with
/// <c>Inventory.BatchQuantityEditMoved</c> (stock count), a cost change with
/// <c>Inventory.BatchCostEditMoved</c> (receipt cost correction) and a supplier change with
/// <c>Inventory.BatchSupplierEditMoved</c> (supplier reassignment).
/// </remarks>
public sealed class UpdateInventoryBatchCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IBatchRetailTermsReviser retailTermsReviser,
    IUnitOfWork unitOfWork,
    IMessageBus messageBus,
    IExpiryPolicyFence expiryFence)
{
    /// <summary>
    /// Reference on the movement that carried a legacy correction's quantity delta. No longer
    /// written; kept because historical movements still carry it.
    /// </summary>
    public const string CorrectionReference = "Batch Correction";

    public async Task<ErrorOr<Success>> Handle(UpdateInventoryBatchCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
            return Error.NotFound("User.NotFound", "User not found.");

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(command.UserId, command.ShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role != ShopRole.Owner && membership.Role != ShopRole.Manager)
            return Error.Forbidden("Shop.PermissionDenied", "Only owners or managers can update batches.");

        InventoryBatchPricingChangedDomainEvent? pricingChanged = null;
        var result = await PostingTransaction.RunAsync<Success>(unitOfWork, async ct =>
        {
            var expiryLock = await expiryFence.AcquireAsync(command.ShopId, true, ct);
            if (expiryLock.IsError) return expiryLock.Errors;
            var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
            if (batch is null || batch.ShopId != command.ShopId)
                return Errors.Inventory.BatchNotFound;

            if (batch.IsVoided)
                return Error.Validation("Inventory.BatchVoided", "Cannot correct an already voided batch.");

            if ((batch.ExpiryDate != command.ExpiryDate || batch.ManufacturingDate != command.ManufacturingDate) && command.ExpectedVersion is not > 0)
                return Errors.Inventory.BatchVersionConflict;
            if (command.ExpectedVersion is { } expectedVersion && expectedVersion != batch.Version)
                return Errors.Inventory.BatchVersionConflict;

            if (command.Quantity != batch.Quantity)
                return Errors.Inventory.BatchQuantityEditMoved;

            if (command.CostPrice != batch.CostPrice)
                return Errors.Inventory.BatchCostEditMoved;

            // A null supplier used to mean "no supplier"; it now means "leave it as it is".
            if (command.SupplierId is { } supplierId && supplierId != batch.SupplierId)
                return Errors.Inventory.BatchSupplierEditMoved;

            var batchNumber = string.IsNullOrWhiteSpace(command.NewBatchNumber) ? batch.BatchNumber : command.NewBatchNumber;
            var revision = await retailTermsReviser.ReviseAsync(
                batch,
                new BatchRetailTerms(
                    batchNumber,
                    command.Mrp,
                    command.SalesPrice,
                    command.TaxRatePercent,
                    command.TaxIncluded,
                    command.ExpiryDate,
                    command.ManufacturingDate),
                command.Notes,
                command.UserId,
                cancellationToken);
            if (revision.IsError)
                return revision.Errors;

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Errors.Inventory.ConcurrentChange;
            }

            if (revision.Value.PricingChanged)
                pricingChanged = new InventoryBatchPricingChangedDomainEvent(command.ShopId, batch.ItemId, batch.Id);

            return Result.Success;
        }, cancellationToken);
        if (!result.IsError && pricingChanged is not null)
            await messageBus.PublishAsync(pricingChanged);
        return result;
    }
}
