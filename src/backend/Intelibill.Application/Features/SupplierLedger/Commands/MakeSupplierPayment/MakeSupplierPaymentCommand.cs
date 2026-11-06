using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;

/// <param name="Method">How the money moved; absent means it was not recorded (never assumed cash).</param>
/// <param name="Allocations">Purchase receipts this payment settles; the rest stays unallocated credit.</param>
public sealed record MakeSupplierPaymentCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid SupplierId,
    decimal Amount,
    DateOnly PaymentDate,
    string? Notes,
    string? OperationKey = null,
    ReceiptTenderMethod? Method = null,
    Guid? BankAccountId = null,
    string? ExternalReference = null,
    IReadOnlyList<PaymentAllocationRequest>? Allocations = null);
