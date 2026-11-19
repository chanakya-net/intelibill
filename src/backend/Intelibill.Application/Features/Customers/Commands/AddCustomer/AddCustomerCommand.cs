using Intelibill.Application.Common.Security;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.Commands.AddCustomer;

/// <param name="CreditPolicyMode">Absent: legacy advisory (an ambiguous request stays visibly unenforced).</param>
/// <param name="CreditTermsDays">Days to pay a due sale; read only with <paramref name="CreditPolicyMode"/>.</param>
public sealed record AddCustomerCommand(
    SessionClaims Session,
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive,
    decimal CreditLimit,
    string? Gstin = null,
    string? StateCode = null,
    CreditPolicyMode? CreditPolicyMode = null,
    int? CreditTermsDays = null)
{
    public Guid ShopId => Session.ActiveShopId.GetValueOrDefault();
    public Guid ActorUserId => Session.UserId;
}
