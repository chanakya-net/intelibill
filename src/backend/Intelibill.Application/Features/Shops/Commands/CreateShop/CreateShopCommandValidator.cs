using FluentValidation;
using System.Text.RegularExpressions;

namespace Intelibill.Application.Features.Shops.Commands.CreateShop;

public sealed class CreateShopCommandValidator : AbstractValidator<CreateShopCommand>
{
    private static readonly Regex IndiaGstRegex = new(
        "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    public CreateShopCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode("Shop.NameRequired")
            .MaximumLength(120);

        RuleFor(x => x.Address)
            .NotEmpty().WithErrorCode("Shop.AddressRequired")
            .MaximumLength(320);

        RuleFor(x => x.City)
            .NotEmpty().WithErrorCode("Shop.CityRequired")
            .MaximumLength(120);

        RuleFor(x => x.State)
            .NotEmpty().WithErrorCode("Shop.StateRequired")
            .MaximumLength(120);

        RuleFor(x => x.Pincode)
            .NotEmpty().WithErrorCode("Shop.PincodeRequired")
            .MaximumLength(16);

        RuleFor(x => x.ContactPerson)
            .MaximumLength(120);

        RuleFor(x => x.MobileNumber)
            .MaximumLength(32);

        RuleFor(x => x.GstNumber)
            .MaximumLength(20)
            .Must(value => string.IsNullOrWhiteSpace(value) || IndiaGstRegex.IsMatch(value.Trim()))
            .WithErrorCode("Shop.GstNumberInvalid")
            .WithMessage("GST number must be a valid Indian GSTIN.");
    }
}
