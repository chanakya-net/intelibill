namespace Intelibill.Application.Features.Tills.Commands.OpenTill;

public sealed record OpenTillCommand(Guid ShopId, Guid ActorUserId, Guid TillId, Guid CashierId, decimal OpeningFloat, string OperationKey, string? OverrideReason = null);
