namespace Intelibill.Application.Features.PostingPeriods.Commands.ClosePostingPeriod;

public sealed record ClosePostingPeriodCommand(
    Guid ShopId,
    Guid ActorUserId,
    DateOnly PeriodStart,
    string Reason,
    string OperationKey);
