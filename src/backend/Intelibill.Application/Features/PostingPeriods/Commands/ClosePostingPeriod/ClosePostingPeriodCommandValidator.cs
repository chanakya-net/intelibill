using FluentValidation;
using Intelibill.Application.Features.PostingPeriods.Services;

namespace Intelibill.Application.Features.PostingPeriods.Commands.ClosePostingPeriod;

internal sealed class ClosePostingPeriodCommandValidator : AbstractValidator<ClosePostingPeriodCommand>
{
    public ClosePostingPeriodCommandValidator()
    {
        RuleFor(x => x.PeriodStart).MustStartAMonth();
        RuleFor(x => x.Reason).MustBeAReason();
        RuleFor(x => x.OperationKey).MustBeAnOperationKey();
    }
}
