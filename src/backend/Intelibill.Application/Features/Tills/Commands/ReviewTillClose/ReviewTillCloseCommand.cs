namespace Intelibill.Application.Features.Tills.Commands.ReviewTillClose;

public sealed record ReviewTillCloseCommand(Guid ShopId, Guid ActorUserId, Guid SessionId, string Reason, uint ExpectedVersion, string OperationKey);
