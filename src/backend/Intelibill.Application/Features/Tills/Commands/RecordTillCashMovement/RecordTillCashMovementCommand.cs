namespace Intelibill.Application.Features.Tills.Commands.RecordTillCashMovement;

public sealed record RecordTillCashMovementCommand(Guid ShopId, Guid ActorUserId, Guid SessionId, decimal Direction, decimal Amount, string Reason, uint ExpectedVersion, string OperationKey, string? OverrideReason = null);
