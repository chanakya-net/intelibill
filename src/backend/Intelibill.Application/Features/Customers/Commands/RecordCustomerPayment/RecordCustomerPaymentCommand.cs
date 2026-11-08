using Intelibill.Application.Features.Tills;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.Commands.RecordCustomerPayment;

/// <param name="Method">How the money moved; absent means it was not recorded (never assumed cash).</param>
/// <param name="Allocations">Invoices this payment settles; whatever is left stays unallocated credit.</param>
public sealed record RecordCustomerPaymentCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid CustomerId,
    decimal Amount,
    DateOnly PaymentDate,
    string? Notes,
    string? OperationKey = null,
    ReceiptTenderMethod? Method = null,
    Guid? BankAccountId = null,
    string? ExternalReference = null,
    IReadOnlyList<PaymentAllocationRequest>? Allocations = null,
    TillPostingContext? Till = null);
