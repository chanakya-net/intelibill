using FluentValidation;

namespace Intelibill.Application.Features.Users.Commands.UpdateMyProfile;

internal sealed class UpdateMyProfileCommandValidator : AbstractValidator<UpdateMyProfileCommand>
{
    private static readonly string[] SupportedLanguages = ["en-IN", "hi-IN", "ta-IN", "te-IN", "bn-IN", "ml-IN"];

    public UpdateMyProfileCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(32)
            .Matches("^\\+?[0-9]{7,15}$")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Language)
            .NotEmpty()
            .Must(language => SupportedLanguages.Contains(language))
            .WithMessage("Language must be one of the supported locale codes.");
    }
}
