using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Features.SupplierLedger.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;

public sealed partial class MakeSupplierPaymentCommandHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    ISupplierLedgerEntryRepository ledgerRepository,
    IExpenseCategoryRepository expenseCategoryRepository,
    IExpenseRepository expenseRepository,
    IUnitOfWork unitOfWork,
    IFinancialOperationExecutor operationExecutor,
    ILogger<MakeSupplierPaymentCommandHandler> logger)
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

        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null || supplier.ShopId != command.ActiveShopId)
            return Errors.Supplier.SupplierNotFound;

        var operationKey = FinancialOperationHasher.NormalizeKey(command.OperationKey);
        if (operationKey is null)
        {
            LogUnkeyedPayment(command.ActiveShopId);
            return await PostAsync(command, cancellationToken);
        }

        var requestHash = FinancialOperationHasher.Compute(new SupplierPaymentPayload(
            command.SupplierId,
            FinancialOperationHasher.NormalizeAmount(command.Amount),
            command.PaymentDate,
            string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim()));

        return await operationExecutor.ExecuteAsync(
            new FinancialOperationScope(command.ActiveShopId, FinancialOperationType.SupplierPayment, operationKey),
            command.ActorUserId,
            command.SupplierId,
            requestHash,
            ct => PostAsync(command, ct),
            cancellationToken);
    }

    private async Task<ErrorOr<SupplierLedgerEntryDto>> PostAsync(
        MakeSupplierPaymentCommand command,
        CancellationToken cancellationToken)
    {
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

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Legacy unkeyed financial command: supplier payment made without an operation key in shop {ShopId}.")]
    private partial void LogUnkeyedPayment(Guid shopId);

    private sealed record SupplierPaymentPayload(Guid SupplierId, decimal Amount, DateOnly PaymentDate, string? Notes);
}
