namespace Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;

public sealed record MakeSupplierPaymentCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid SupplierId,
    decimal Amount,
    DateOnly PaymentDate,
    string? Notes);
