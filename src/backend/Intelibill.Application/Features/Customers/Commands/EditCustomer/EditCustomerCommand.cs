namespace Intelibill.Application.Features.Customers.Commands.EditCustomer;

public sealed record EditCustomerCommand(
    Guid ActorUserId,
    Guid ShopId,
    Guid CustomerId,
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive);
