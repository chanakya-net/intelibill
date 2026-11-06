using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.Commands.EditCustomer;

/// <param name="CreditPolicyMode">Absent: the stored policy and terms are kept.</param>
/// <param name="CreditTermsDays">Days to pay a due sale; read only with <paramref name="CreditPolicyMode"/>.</param>
/// <param name="ActorUserId">Who asks; credit settings need an owner or manager. Null = trusted internal caller.</param>
public sealed record EditCustomerCommand(
    Guid ShopId,
    Guid CustomerId,
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive,
    decimal CreditLimit,
    string? Gstin = null,
    string? StateCode = null,
    CreditPolicyMode? CreditPolicyMode = null,
    int? CreditTermsDays = null,
    Guid? ActorUserId = null);
