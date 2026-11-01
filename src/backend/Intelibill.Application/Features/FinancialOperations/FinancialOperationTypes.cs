using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.FinancialOperations;

/// <summary>Route slugs for <see cref="FinancialOperationType"/>; the web client uses the same ones.</summary>
public static class FinancialOperationTypes
{
    public const string CustomerPayment = "customer-payment";
    public const string SupplierPayment = "supplier-payment";

    public static FinancialOperationType? FromSlug(string slug) => slug switch
    {
        CustomerPayment => FinancialOperationType.CustomerPayment,
        SupplierPayment => FinancialOperationType.SupplierPayment,
        _ => null,
    };

    public static string ToSlug(FinancialOperationType type) => type switch
    {
        FinancialOperationType.CustomerPayment => CustomerPayment,
        FinancialOperationType.SupplierPayment => SupplierPayment,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
