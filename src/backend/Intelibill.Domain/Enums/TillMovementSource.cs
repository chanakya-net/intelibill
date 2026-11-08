namespace Intelibill.Domain.Enums;

public enum TillMovementSource
{
    OnlineSale = 1,
    CustomerPayment = 2,
    SupplierPayment = 3,
    Expense = 4,
    ExpenseCorrection = 5,
    SaleReturn = 6,
    SaleReturnVoid = 7,
    SaleCancellation = 8,
    CustomerPaymentReversal = 9,
    SupplierPaymentReversal = 10,
    ManualCash = 11
}
