using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Services.Commands.AddService;

internal sealed class AddServiceCommandValidator : AbstractValidator<AddServiceCommand>
{
    public AddServiceCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode(Errors.Service.NameRequired.Code)
            .MaximumLength(180);

        RuleFor(x => x.Description)
            .MaximumLength(1000);

        RuleFor(x => x.Price)
            .GreaterThan(0m).WithErrorCode(Errors.Service.PriceInvalid.Code);

        RuleFor(x => x.HsnCode)
            .MaximumLength(20)
            .Must(value => value is null || IsValidHsnCode(value))
            .WithErrorCode(Errors.Service.HsnCodeInvalid.Code)
            .When(x => !string.IsNullOrWhiteSpace(x.HsnCode));

        RuleFor(x => x.TaxRatePercent)
            .InclusiveBetween(0m, 100m).WithErrorCode(Errors.Service.TaxRateInvalid.Code);
    }

    private static bool IsValidHsnCode(string value)
    {
        var normalized = value.Trim();
        return normalized.Length is >= 4 and <= 8 && normalized.All(char.IsDigit);
    }
}
