using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.ValueObjects;

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

        RuleFor(x => x.QuantityScale)
            .InclusiveBetween(0, QuantityUnits.MaxScale)
            .WithErrorCode(Errors.Item.QuantityScaleInvalid.Code)
            .When(x => x.QuantityScale.HasValue);

        RuleFor(x => x)
            .Must(x => QuantityUnits.Resolve(x.Uom)!.Scale == x.QuantityScale)
            .OverridePropertyName(nameof(UpdateItemCommand.QuantityScale))
            .WithErrorCode(Errors.Item.QuantityScaleMismatch.Code)
            .WithMessage(Errors.Item.QuantityScaleMismatch.Description)
            .When(x => x.QuantityScale is >= 0 and <= QuantityUnits.MaxScale && QuantityUnits.Resolve(x.Uom) is not null);

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
