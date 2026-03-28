using FluentValidation;

namespace Intelibill.Application.Features.Shops.Commands.UpdateShop;

internal sealed class UpdateShopCommandValidator : AbstractValidator<UpdateShopCommand>
{
    public UpdateShopCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.Address)
            .NotEmpty()
            .MaximumLength(320);

        RuleFor(x => x.City)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.State)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.Pincode)
            .NotEmpty()
            .MaximumLength(16);

        RuleFor(x => x.ContactPerson)
            .MaximumLength(120);

        RuleFor(x => x.MobileNumber)
            .MaximumLength(32);
    }
}
