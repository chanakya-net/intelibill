using FluentValidation;
using Intelibill.Application.Common.Errors;
using System.Text.RegularExpressions;

namespace Intelibill.Application.Features.BankAccounts.Commands.AddBankAccount;

public sealed class AddBankAccountCommandValidator : AbstractValidator<AddBankAccountCommand>
{
    private static readonly Regex IfscRegex = new("^[A-Z]{4}0[A-Z0-9]{6}$", RegexOptions.Compiled);

    public AddBankAccountCommandValidator()
    {
        RuleFor(x => x.ShopId)
            .NotEmpty();

        RuleFor(x => x.IfscCode)
            .Matches(IfscRegex)
            .WithErrorCode(Errors.BankAccount.IfscCodeInvalid.Code)
            .WithMessage(Errors.BankAccount.IfscCodeInvalid.Description)
            .When(x => !string.IsNullOrWhiteSpace(x.IfscCode));

        RuleFor(x => x.AccountType)
            .Must(type => string.IsNullOrWhiteSpace(type) ||
                          Enum.TryParse<Intelibill.Domain.Enums.BankAccountType>(type, true, out _))
            .WithErrorCode(Errors.BankAccount.BankAccountTypeInvalid.Code)
            .WithMessage(Errors.BankAccount.BankAccountTypeInvalid.Description)
            .When(x => !string.IsNullOrWhiteSpace(x.AccountType));
    }
}
