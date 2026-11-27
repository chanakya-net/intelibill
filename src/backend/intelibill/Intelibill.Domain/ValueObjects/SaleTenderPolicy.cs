using ErrorOr;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.ValueObjects;

/// <summary>Conserves the legacy external paid amount without treating due or credit as drawer cash.</summary>
public static class SaleTenderPolicy
{
    public static ErrorOr<Success> Validate(PaymentMethod method, decimal paidAmount, IReadOnlyList<TenderLine>? lines)
    {
        if (lines is { Count: > 1 })
            return Error.Validation("Till.MixedTenderUnsupported", "Mixed tender is unavailable.");
        if (lines is null && paidAmount > 0 || lines?.Any(line => line is null) == true)
            return Error.Validation("Till.TenderRequired", "A known external tender is required.");
        var valid = TenderLine.ValidateLines(lines ?? [], paidAmount);
        if (valid.IsError) return valid.Errors;
        if (paidAmount == 0) return Result.Success;
        // These enums deliberately have different numeric values. Credit is never an external tender.
        var expected = method switch
        {
            PaymentMethod.Cash => ReceiptTenderMethod.Cash,
            PaymentMethod.UPI => ReceiptTenderMethod.Upi,
            PaymentMethod.Card => ReceiptTenderMethod.Card,
            _ => ReceiptTenderMethod.UnknownRecordedMethod
        };
        return expected != ReceiptTenderMethod.UnknownRecordedMethod && lines![0].Method == expected
            ? Result.Success
            : Error.Validation("Till.TenderMethodMismatch", "Tender must match the sale's recorded external payment method.");
    }
}
