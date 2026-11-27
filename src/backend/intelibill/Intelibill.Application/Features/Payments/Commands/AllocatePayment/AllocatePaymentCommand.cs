using Intelibill.Application.Features.Payments.DTOs;

namespace Intelibill.Application.Features.Payments.Commands.AllocatePayment;

public sealed record AllocatePaymentCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid ReceiptId,
    IReadOnlyList<PaymentAllocationRequest> Allocations,
    string? OperationKey = null);
