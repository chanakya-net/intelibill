namespace Intelibill.Application.Features.PostingPeriods.DTOs;

public sealed record PostingPeriodEventDto(string Action, string Reason, Guid ActorUserId, DateTimeOffset OccurredAt);

public sealed record PostingPeriodDto(
    DateOnly StartDate,
    DateOnly EndDate,
    string State,
    DateTimeOffset? ClosedAt,
    Guid? ClosedByUserId,
    IReadOnlyList<PostingPeriodEventDto> Events);

public sealed record PostingPeriodListDto(int FiscalYearStart, IReadOnlyList<PostingPeriodDto> Periods);
