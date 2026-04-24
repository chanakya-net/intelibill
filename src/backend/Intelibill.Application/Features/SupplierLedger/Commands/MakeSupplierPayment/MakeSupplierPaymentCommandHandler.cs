using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.SupplierLedger.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;

public sealed class MakeSupplierPaymentCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISupplierRepository supplierRepository,
    ISupplierLedgerEntryRepository ledgerRepository,
    IExpenseCategoryRepository expenseCategoryRepository,
    IExpenseRepository expenseRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<SupplierLedgerEntryDto>> HandleAsync(
        MakeSupplierPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var actorMembership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ActiveShopId);
        if (actorMembership is null)
            return Errors.Shop.MembershipNotFound;

        if (actorMembership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.Supplier.UserIsNotOwnerOrManager;

        var shop = await shopRepository.GetByIdWithMembersAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var ownerMembership = shop.Memberships.FirstOrDefault(sm => sm.Role == ShopRole.Owner);
        if (ownerMembership is null)
            return Errors.Supplier.ShopOwnerNotFound;

        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null || supplier.OwnerUserId != ownerMembership.UserId)
            return Errors.Supplier.SupplierNotFound;

        var entryOrError = SupplierLedgerEntry.Create(
            command.ActiveShopId,
            command.SupplierId,
            batchId: null,
            SupplierLedgerEntryType.PaymentMade,
            amount: command.Amount,
            command.PaymentDate,
            command.Notes,
            command.ActorUserId);

        if (entryOrError.IsError)
            return entryOrError.Errors;

        const string categoryName = "Supplier Payments";
        var category = await expenseCategoryRepository.GetByNameAsync(command.ActiveShopId, categoryName, cancellationToken);
        if (category is null)
        {
            category = ExpenseCategory.Create(command.ActiveShopId, categoryName, DateTimeOffset.UtcNow);
            await expenseCategoryRepository.AddAsync(category, cancellationToken);
        }

        var entry = entryOrError.Value;
        var expense = Expense.CreateFromSupplierPayment(
            command.ActiveShopId,
            category.Id,
            command.Amount,
            "Supplier",
            command.Notes,
            command.PaymentDate,
            command.ActorUserId,
            entry.Id);

        await ledgerRepository.AddAsync(entry, cancellationToken);
        await expenseRepository.AddAsync(expense, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var e = entry;
        return new SupplierLedgerEntryDto(e.Id, e.SupplierId, e.EntryType, e.Amount, e.EntryDate, e.Notes);
    }
}
