using FluentValidation;

namespace InventoryAI.Application.Features.Auth.Commands.RequestPasswordReset;

internal sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.AppBaseUrl)
            .NotEmpty();
    }
}
