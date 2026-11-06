using ErrorOr;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.PostingPeriods.DTOs;
using Intelibill.Application.Features.PostingPeriods.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PostingPeriods.Commands.ClosePostingPeriod;

/// <summary>
/// Closes a finished month. Owner only, with a reason and an operation key; the month's row is
/// locked <c>FOR UPDATE</c> so the close waits for every authorized posting to commit first.
/// </summary>
public sealed class ClosePostingPeriodCommandHandler(
    IShopRepository shopRepository,
    IPostingPeriodRepository periodRepository,
    IFinancialOperationExecutor operationExecutor,
    IUnitOfWork unitOfWork,
    IBusinessClock businessClock)
{
    private static readonly ClosePostingPeriodCommandValidator Validator = new();

    public async Task<ErrorOr<PostingPeriodDto>> HandleAsync(
        ClosePostingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (!validation.IsValid)
            return validation.Errors.Select(e => Error.Validation(e.ErrorCode, e.ErrorMessage)).ToList();

        var runner = new PostingPeriodTransitionRunner(shopRepository, periodRepository, operationExecutor, unitOfWork, businessClock);
        return await runner.RunAsync(
            FinancialOperationType.PostingPeriodClose,
            command.ShopId,
            command.ActorUserId,
            command.PeriodStart,
            command.Reason,
            command.OperationKey,
            (period, now, today) => period.Close(command.ActorUserId, now, today, command.Reason),
            cancellationToken);
    }
}
