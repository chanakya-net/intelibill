using FluentValidation;
using Intelibill.Application.Features.PostingPeriods.Services;

namespace Intelibill.Application.Features.PostingPeriods.Commands.ReopenPostingPeriod;

internal sealed class ReopenPostingPeriodCommandValidator : AbstractValidator<ReopenPostingPeriodCommand>
{
    public ReopenPostingPeriodCommandValidator()
    {
        RuleFor(x => x.PeriodStart).MustStartAMonth();
        RuleFor(x => x.Reason).MustBeAReason();
        RuleFor(x => x.OperationKey).MustBeAnOperationKey();
    }
}
