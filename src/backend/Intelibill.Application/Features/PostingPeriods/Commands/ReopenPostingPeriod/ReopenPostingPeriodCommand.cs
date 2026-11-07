namespace Intelibill.Application.Features.PostingPeriods.Commands.ReopenPostingPeriod;

public sealed record ReopenPostingPeriodCommand(
    Guid ShopId,
    Guid ActorUserId,
    DateOnly PeriodStart,
    string Reason,
    string OperationKey);
