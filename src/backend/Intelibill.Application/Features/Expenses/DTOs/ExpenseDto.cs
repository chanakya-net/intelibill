namespace Intelibill.Application.Features.Expenses.DTOs;

public sealed record ExpenseDto(
    Guid Id,
    Guid ShopId,
    Guid CategoryId,
    string CategoryName,
    decimal Amount,
    string PaidTo,
    string? Description,
    DateOnly ExpenseDate,
    Guid ActorUserId,
    bool IsVoided,
    Guid? OriginalExpenseId,
    Guid? SupplierLedgerEntryId,
    DateTimeOffset CreatedAt);
