using FluentValidation;

namespace InventoryAI.Application.Features.Auth.Commands.ExternalLogin;

internal sealed class ExternalLoginCommandValidator : AbstractValidator<ExternalLoginCommand>
{
    public ExternalLoginCommandValidator()
    {
        RuleFor(x => x.Provider).IsInEnum();
        RuleFor(x => x.Token).NotEmpty();
    }
}
