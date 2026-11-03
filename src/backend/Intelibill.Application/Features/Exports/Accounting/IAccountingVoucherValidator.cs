using ErrorOr;

namespace Intelibill.Application.Features.Exports.Accounting;

/// <summary>
/// The gate every voucher passes before it is rendered: a voucher that does not balance is refused
/// with what is wrong, never balanced by an invented line.
/// </summary>
public interface IAccountingVoucherValidator
{
    ErrorOr<Success> Validate(AccountingVoucher voucher);
}
