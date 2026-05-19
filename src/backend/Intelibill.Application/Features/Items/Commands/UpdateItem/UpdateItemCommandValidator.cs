using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Items.Commands.UpdateItem;

public sealed class UpdateItemCommandValidator : AbstractValidator<UpdateItemCommand>
{
    public UpdateItemCommandValidator()
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

        RuleFor(x => x.HsnCode)
            .MaximumLength(20)
            .Must(value => value is null || IsValidHsnCode(value))
            .When(x => !string.IsNullOrWhiteSpace(x.HsnCode));

        RuleFor(x => x.DefaultTaxRatePercent)
            .InclusiveBetween(0m, 100m);
    }

    private static bool IsValidHsnCode(string value)
    {
        var normalized = value.Trim();
        return normalized.Length is >= 4 and <= 8 && normalized.All(char.IsDigit);
    }
}
