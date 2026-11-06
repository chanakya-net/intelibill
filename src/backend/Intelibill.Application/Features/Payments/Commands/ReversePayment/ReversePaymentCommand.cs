namespace Intelibill.Application.Features.Payments.Commands.ReversePayment;

public sealed record ReversePaymentCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid ReceiptId,
    string Reason,
    string OperationKey);
