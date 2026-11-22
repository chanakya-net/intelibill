using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using ErrorOr;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.PostingPeriods.DTOs;
using Intelibill.Application.Features.PostingPeriods.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PostingPeriods.Commands.ReopenPostingPeriod;

/// <summary>Reopens a closed month. Owner only, with a reason and an operation key.</summary>
public sealed class ReopenPostingPeriodCommandHandler(
    IShopRepository shopRepository,
    IPostingPeriodRepository periodRepository,
    IFinancialOperationExecutor operationExecutor,
    IUnitOfWork unitOfWork,
    IBusinessClock businessClock,
    ILiveSessionAuthority authority)
{
    private static readonly ReopenPostingPeriodCommandValidator Validator = new();

    [LicenceAction(LicenceAction.SourceCorrection)]
    public async Task<ErrorOr<PostingPeriodDto>> HandleAsync(
        ReopenPostingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (!validation.IsValid)
            return validation.Errors.Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage)).ToList();

        var runner = new PostingPeriodTransitionRunner(shopRepository, periodRepository, operationExecutor, unitOfWork, businessClock, authority);
        return await runner.RunAsync(
            FinancialOperationType.PostingPeriodReopen,
            command.ShopId,
            command.ActorUserId,
            command.PeriodStart,
            command.Reason,
            command.OperationKey,
            command.Session,
            (period, now, _) => period.Reopen(command.ActorUserId, now, command.Reason),
            cancellationToken);
    }
}
