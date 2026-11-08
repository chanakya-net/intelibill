using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Common.Posting;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Events;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Intelibill.Application.Features.Inventory.Commands.UpdateBatchPrices;

/// <summary>
/// Revises a batch's retail prices and lot details in place.
/// </summary>
/// <remarks>
/// The batch keeps its identity, quantity, cost and supplier, so its receipt payable, its stock and
/// every sale already made from it (and their returns) are untouched. A price change appends a
/// <see cref="Domain.Entities.BatchPriceRevision"/>. The edit is checked against
/// <c>ExpectedVersion</c> even when no quantity moves: two editors of one version cannot both win.
/// </remarks>
public sealed class UpdateBatchPricesCommandHandler(
    IUserRepository userRepository,
    IInventoryBatchRepository inventoryBatchRepository,
    IBatchRetailTermsReviser retailTermsReviser,
    IUnitOfWork unitOfWork,
    IMessageBus messageBus,
    IExpiryPolicyFence expiryFence)
{
    private static readonly UpdateBatchPricesCommandValidator Validator = new();

    public async Task<ErrorOr<BatchPriceUpdateResultDto>> HandleAsync(
        UpdateBatchPricesCommand command,
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
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Inventory.UserIsNotOwnerOrManager;

        InventoryBatchPricingChangedDomainEvent? pricingChanged = null;
        var result = await PostingTransaction.RunAsync<BatchPriceUpdateResultDto>(unitOfWork, async ct =>
        {
            var expiryLock = await expiryFence.AcquireAsync(command.ActiveShopId, true, ct);
            if (expiryLock.IsError) return expiryLock.Errors;
            var batch = await inventoryBatchRepository.GetByIdAsync(command.BatchId, cancellationToken);
            if (batch is null || batch.ShopId != command.ActiveShopId)
                return Errors.Inventory.BatchNotFound;

            if (batch.IsVoided)
                return Errors.Inventory.BatchAlreadyVoided;

            if (batch.Version != command.ExpectedVersion)
                return Errors.Inventory.BatchVersionConflict;

            var revision = await retailTermsReviser.ReviseAsync(
                batch,
                new BatchRetailTerms(
                    command.BatchNumber ?? batch.BatchNumber,
                    command.Mrp,
                    command.SalesPrice,
                    command.TaxRatePercent,
                    command.TaxIncluded,
                    command.ExpiryDate,
                    command.ManufacturingDate),
                command.Reason,
                command.ActorUserId,
                cancellationToken);
            if (revision.IsError)
                return revision.Errors;

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Errors.Inventory.BatchVersionConflict;
            }

            if (revision.Value.PricingChanged)
                pricingChanged = new InventoryBatchPricingChangedDomainEvent(command.ActiveShopId, batch.ItemId, batch.Id);

            return new BatchPriceUpdateResultDto(batch.Id, batch.Version, revision.Value.PriceRevision?.Id);
        }, cancellationToken);
        if (!result.IsError && pricingChanged is not null)
            await messageBus.PublishAsync(pricingChanged);
        return result;
    }
}
