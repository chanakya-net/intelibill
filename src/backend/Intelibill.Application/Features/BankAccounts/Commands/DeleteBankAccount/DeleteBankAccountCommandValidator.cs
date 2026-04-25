using FluentValidation;

namespace Intelibill.Application.Features.BankAccounts.Commands.DeleteBankAccount;

public sealed class DeleteBankAccountCommandValidator : AbstractValidator<DeleteBankAccountCommand>
{
    public DeleteBankAccountCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ShopId).NotEmpty();
    }
}
