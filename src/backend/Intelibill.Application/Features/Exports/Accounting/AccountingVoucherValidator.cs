using ErrorOr;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Exports.Accounting;

public sealed class AccountingVoucherValidator : IAccountingVoucherValidator
{
    public ErrorOr<Success> Validate(AccountingVoucher voucher)
    {
        if (voucher.Lines.Count < 2)
            return Errors.Export.VoucherInvalid(voucher.Number, "it needs at least one debit and one credit line");

        foreach (var line in voucher.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Ledger))
                return Errors.Export.VoucherInvalid(voucher.Number, "a line names no ledger");

            if (line.Amount <= 0m || decimal.Round(line.Amount, 2) != line.Amount)
                return Errors.Export.VoucherInvalid(
                    voucher.Number,
                    $"the {line.Ledger} line amount {line.Amount} is not a positive amount in paise");
        }

        return voucher.Debits == voucher.Credits
            ? Result.Success
            : Errors.Export.VoucherUnbalanced(voucher.Number, voucher.Debits, voucher.Credits);
    }
}
