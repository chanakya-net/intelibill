using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class Expense : BaseEntity
{
    public TillSettlementEvidence? SettlementEvidence { get; private set; }
    public ExpenseTreatment Treatment { get; private set; }
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
        Guid actorUserId,
        ExpenseTreatment treatment = ExpenseTreatment.Unclassified,
        TillSettlementEvidence? settlementEvidence = null)
    {
        return new Expense
        {
            Treatment = treatment,
            SettlementEvidence = settlementEvidence,
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
        Guid originalExpenseId,
        ExpenseTreatment treatment = ExpenseTreatment.Unclassified,
        Guid? supplierLedgerEntryId = null,
        TillSettlementEvidence? settlementEvidence = null)
    {
        var expense = Create(shopId, categoryId, amount, paidTo, description, expenseDate, actorUserId, treatment, settlementEvidence);
        expense.OriginalExpenseId = originalExpenseId;
        expense.Treatment = treatment;
        expense.SupplierLedgerEntryId = supplierLedgerEntryId;
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
        expense.Treatment = ExpenseTreatment.SupplierSettlement;
        return expense;
    }

    public void MarkVoided()
    {
        IsVoided = true;
    }
}
