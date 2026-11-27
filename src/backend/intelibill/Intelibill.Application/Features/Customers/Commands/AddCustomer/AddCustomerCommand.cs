using Intelibill.Application.Common.Security;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.Commands.AddCustomer;

/// <param name="CreditPolicyMode">Required: how new due is judged against <paramref name="CreditLimit"/>.</param>
/// <param name="CreditTermsDays">Days to pay a due sale; null when no terms were agreed.</param>
public sealed record AddCustomerCommand(
    SessionClaims Session,
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive,
    decimal CreditLimit,
    CreditPolicyMode CreditPolicyMode,
    string? Gstin = null,
    string? StateCode = null,
    int? CreditTermsDays = null)
{
    public Guid ShopId => Session.ActiveShopId.GetValueOrDefault();
    public Guid ActorUserId => Session.UserId;
}
