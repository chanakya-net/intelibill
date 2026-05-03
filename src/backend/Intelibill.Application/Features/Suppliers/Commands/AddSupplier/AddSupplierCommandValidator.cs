using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Suppliers.Commands.AddSupplier;

internal sealed class AddSupplierCommandValidator : AbstractValidator<AddSupplierCommand>
{
    public AddSupplierCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(Errors.Supplier.NameRequired.Code)
            .MaximumLength(180);

        RuleFor(x => x.ContactPersonName)
            .MaximumLength(120);

        RuleFor(x => x.ContactPersonPhone)
            .MaximumLength(32)
            .Matches("^\\+?[0-9]{7,15}$")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactPersonPhone))
            .WithErrorCode(Errors.Supplier.ContactPersonPhoneInvalid.Code);

        RuleFor(x => x.Address)
            .NotEmpty().WithErrorCode(Errors.Supplier.AddressRequired.Code)
            .MaximumLength(320);

        RuleFor(x => x.City)
            .NotEmpty().WithErrorCode(Errors.Supplier.CityRequired.Code)
            .MaximumLength(120);

        RuleFor(x => x.State)
            .NotEmpty().WithErrorCode(Errors.Supplier.StateRequired.Code)
            .MaximumLength(120);

        RuleFor(x => x.Pin)
            .NotEmpty().WithErrorCode(Errors.Supplier.PinRequired.Code)
            .MaximumLength(16);
    }
}
