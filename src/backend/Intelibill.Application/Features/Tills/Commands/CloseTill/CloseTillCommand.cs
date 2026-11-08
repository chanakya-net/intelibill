namespace Intelibill.Application.Features.Tills.Commands.CloseTill;

public sealed record CloseTillCommand(Guid ShopId, Guid ActorUserId, Guid SessionId, decimal CountedCash, string? Reason, uint ExpectedVersion, string OperationKey, string? OverrideReason = null, bool FinalizeOverdue = false);
