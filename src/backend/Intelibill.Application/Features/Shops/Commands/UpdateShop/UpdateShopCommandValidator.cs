using FluentValidation;
using System.Text.RegularExpressions;

namespace Intelibill.Application.Features.Shops.Commands.UpdateShop;

internal sealed class UpdateShopCommandValidator : AbstractValidator<UpdateShopCommand>
{
    private static readonly Regex IndiaGstRegex = new(
        "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

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

        RuleFor(x => x.GstNumber)
            .MaximumLength(20)
            .Must(value => string.IsNullOrWhiteSpace(value) || IndiaGstRegex.IsMatch(value.Trim()))
            .WithMessage("GST number must be a valid Indian GSTIN.");
    }
}
