namespace Intelibill.Application.Features.Customers.Commands.RecordCustomerPayment;

public sealed record RecordCustomerPaymentCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid CustomerId,
    decimal Amount,
    DateOnly PaymentDate,
    string? Notes);
