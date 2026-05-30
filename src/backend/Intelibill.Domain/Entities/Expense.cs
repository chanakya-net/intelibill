using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class Expense : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid CategoryId { get; private set; }
    public decimal Amount { get; private set; }
    public string PaidTo { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateOnly ExpenseDate { get; private set; }
    public Guid ActorUserId { get; private set; }
    public bool IsVoided { get; private set; }
    public Guid? OriginalExpenseId { get; private set; }
    public Guid? SupplierLedgerEntryId { get; private set; }

    public ExpenseCategory Category { get; private set; } = null!;

    private Expense() { }

    public static Expense Create(
        Guid shopId,
        Guid categoryId,
        decimal amount,
        string paidTo,
        string? description,
        DateOnly expenseDate,
        Guid actorUserId)
    {
        return new Expense
        {
            Id = Guid.NewGuid(),
            ShopId = shopId,
            CategoryId = categoryId,
            Amount = amount,
            PaidTo = paidTo.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            ExpenseDate = expenseDate,
            ActorUserId = actorUserId,
            IsVoided = false,
            OriginalExpenseId = null,
        };
    }

    public static Expense CreateCorrection(
        Guid shopId,
        Guid categoryId,
        decimal amount,
        string paidTo,
        string? description,
        DateOnly expenseDate,
        Guid actorUserId,
        Guid originalExpenseId)
    {
        var expense = Create(shopId, categoryId, amount, paidTo, description, expenseDate, actorUserId);
        expense.OriginalExpenseId = originalExpenseId;
        return expense;
    }

    public static Expense CreateFromSupplierPayment(
        Guid shopId,
        Guid categoryId,
        decimal amount,
        string paidTo,
        string? description,
        DateOnly expenseDate,
        Guid actorUserId,
        Guid? supplierLedgerEntryId)
    {
        var expense = Create(shopId, categoryId, amount, paidTo, description, expenseDate, actorUserId);
        expense.SupplierLedgerEntryId = supplierLedgerEntryId;
        return expense;
    }

    public void MarkVoided()
    {
        IsVoided = true;
    }
}
