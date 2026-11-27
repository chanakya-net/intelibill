using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Tills;

public sealed record TillSourcePosting(TillMovementSource SourceType, Guid SourceId, string Purpose,
    IReadOnlyList<TenderLine> Lines, decimal Direction, DateTimeOffset OccurredAt, string OperationKey, string? Reason,
    decimal StoreCreditIssued = 0, decimal StoreCreditRedeemed = 0, decimal ReceivableChange = 0);
