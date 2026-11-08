using ErrorOr;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.ValueObjects;

public sealed record TenderLine(ReceiptTenderMethod Method, decimal AppliedAmount, decimal? CashTendered,
    decimal? Change, string? ExternalReference, Guid? BankAccountId)
{
    public ErrorOr<Success> Validate()
    {
        if (!Enum.IsDefined(Method) || Method == ReceiptTenderMethod.UnknownRecordedMethod)
            return Error.Validation("Till.TenderRequired", "A known external tender is required.");
        if (!TillMoney.IsValid(AppliedAmount) || AppliedAmount <= 0)
            return Error.Validation("Till.AmountInvalid", "Tender amount must be positive INR with at most two decimal places.");
        if (BankAccountId == Guid.Empty || ExternalReference is { Length: > 255 })
            return Error.Validation("Till.TenderReferenceInvalid", "Tender references are invalid.");
        if (CashTendered.HasValue != Change.HasValue ||
            (Method != ReceiptTenderMethod.Cash && (CashTendered.HasValue || Change.HasValue)))
            return Error.Validation("Till.CashPairInvalid", "Cash tendered and change must be paired and apply only to cash.");
        if (CashTendered is { } tendered && Change is { } change &&
            (!TillMoney.IsValid(tendered) || !TillMoney.IsValid(change) || change < 0 || tendered < AppliedAmount || tendered - change != AppliedAmount))
            return Error.Validation("Till.CashPairInvalid", "Tendered cash less change must equal the applied amount.");
        return Result.Success;
    }
    public static ErrorOr<Success> ValidateLines(IReadOnlyList<TenderLine> lines, decimal amount)
    {
        if (!TillMoney.IsValid(amount) || amount < 0 || (amount == 0 ? lines.Count != 0 : lines.Count != 1))
            return Error.Validation("Till.SingleTenderRequired", "Exactly one tender is required for a positive external amount; mixed tender is unavailable.");
        if (lines.Count == 0) return Result.Success;
        var valid = lines[0].Validate();
        if (valid.IsError) return valid.Errors;
        return lines[0].AppliedAmount == amount ? Result.Success : Error.Validation("Till.TenderAmountMismatch", "Tender must equal the external settlement amount.");
    }
}
