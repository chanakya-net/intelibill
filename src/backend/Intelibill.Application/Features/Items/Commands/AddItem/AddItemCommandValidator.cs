using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Items.Commands.AddItem;

internal sealed class AddItemCommandValidator : AbstractValidator<AddItemCommand>
{
    public AddItemCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(Errors.Item.NameRequired.Code)
            .MaximumLength(180);

        RuleFor(x => x.Barcode)
            .NotEmpty().WithErrorCode(Errors.Item.BarcodeRequired.Code)
            .MaximumLength(128);

        RuleFor(x => x.Uom)
            .NotEmpty().WithErrorCode(Errors.Item.UomRequired.Code)
            .MaximumLength(32);

        RuleFor(x => x.Description)
            .MaximumLength(1000);
    }
}
