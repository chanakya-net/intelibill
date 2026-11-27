namespace Intelibill.Application.Features.Tills.Commands.CreateTill;

public sealed record CreateTillCommand(Guid ShopId, Guid ActorUserId, string Name, string OperationKey);
