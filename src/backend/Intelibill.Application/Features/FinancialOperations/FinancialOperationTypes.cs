using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.FinancialOperations;

/// <summary>Route slugs for <see cref="FinancialOperationType"/>; the web client uses the same ones.</summary>
public static class FinancialOperationTypes
{
    public const string PurchaseOrderReceipt = "purchase-order-receipt";
    public const string CustomerPayment = "customer-payment";
    public const string SupplierPayment = "supplier-payment";

    public static FinancialOperationType? FromSlug(string slug) => slug switch
    {
        "supplier-return" => FinancialOperationType.SupplierReturn,
        "supplier-return-credit" => FinancialOperationType.SupplierReturnCredit,
        "purchase-receipt-reversal" => FinancialOperationType.PurchaseReceiptReversal,
        PurchaseOrderReceipt => FinancialOperationType.PurchaseOrderReceipt,
        CustomerPayment => FinancialOperationType.CustomerPayment,
        SupplierPayment => FinancialOperationType.SupplierPayment,
        _ => null,
    };

    public static string ToSlug(FinancialOperationType type) => type switch
    {
        FinancialOperationType.SupplierReturn => "supplier-return",
        FinancialOperationType.SupplierReturnCredit => "supplier-return-credit",
        FinancialOperationType.PurchaseReceiptReversal => "purchase-receipt-reversal",
        FinancialOperationType.PurchaseOrderReceipt => PurchaseOrderReceipt,
        FinancialOperationType.CustomerPayment => CustomerPayment,
        FinancialOperationType.SupplierPayment => SupplierPayment,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
