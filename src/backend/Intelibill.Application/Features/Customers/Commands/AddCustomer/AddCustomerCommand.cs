namespace Intelibill.Application.Features.Customers.Commands.AddCustomer;

public sealed record AddCustomerCommand(
    Guid ShopId,
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive);
