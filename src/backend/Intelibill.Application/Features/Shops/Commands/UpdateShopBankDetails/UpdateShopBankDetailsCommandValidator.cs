using FluentValidation;
using System.Text.RegularExpressions;

namespace Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;

public sealed class UpdateShopBankDetailsCommandValidator : AbstractValidator<UpdateShopBankDetailsCommand>
{
    private static readonly Regex IndiaIfscRegex = new(
        "^[A-Z]{4}0[A-Z0-9]{6}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private static readonly string[] ValidAccountTypes = ["Savings", "Current"];

    public UpdateShopBankDetailsCommandValidator()
    {
        RuleFor(x => x.BankName)
            .MaximumLength(120);

        RuleFor(x => x.BankAccountNumber)
            .MaximumLength(50);

        RuleFor(x => x.BankAccountType)
            .Must(value => string.IsNullOrWhiteSpace(value) || ValidAccountTypes.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithErrorCode("Shop.BankAccountTypeInvalid")
            .WithMessage("Bank account type must be Savings or Current.");

        RuleFor(x => x.IfscCode)
            .MaximumLength(20)
            .Must(value => string.IsNullOrWhiteSpace(value) || IndiaIfscRegex.IsMatch(value.Trim()))
            .WithErrorCode("Shop.IfscCodeInvalid")
            .WithMessage("IFSC code must be a valid Indian bank IFSC code.");

        RuleFor(x => x.AccountHolderName)
            .MaximumLength(120);
    }
}
