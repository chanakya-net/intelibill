namespace Intelibill.Application.Features.Payments.Queries.GetPaymentReceipt;

public sealed record GetPaymentReceiptQuery(Guid ActorUserId, Guid ActiveShopId, Guid ReceiptId);
