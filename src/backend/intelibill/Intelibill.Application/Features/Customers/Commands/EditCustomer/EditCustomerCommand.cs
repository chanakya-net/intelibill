using Intelibill.Application.Common.Security;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.Commands.EditCustomer;

/// <summary>Replaces a customer's details: every field is saved as sent.</summary>
/// <param name="CreditPolicyMode">Required: how new due is judged against <paramref name="CreditLimit"/>.</param>
/// <param name="Gstin">The buyer's GSTIN; null or blank records none.</param>
/// <param name="StateCode">The buyer's GST state; null or blank takes the GSTIN's own state, if any.</param>
/// <param name="CreditTermsDays">Days to pay a due sale; null when no terms were agreed.</param>
/// <param name="ActorUserId">Who asks; credit settings need an owner or manager. Null = trusted internal caller.</param>
public sealed record EditCustomerCommand(
    Guid ShopId,
    Guid CustomerId,
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive,
    decimal CreditLimit,
    CreditPolicyMode CreditPolicyMode,
    SessionClaims? Session,
    string? Gstin = null,
    string? StateCode = null,
    int? CreditTermsDays = null,
    Guid? ActorUserId = null);
