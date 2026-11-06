using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.DTOs;

/// <param name="CurrentExposure">The signed account balance now (negative = the customer is in credit).</param>
/// <param name="AvailableCredit">New due the account can take now; null when the limit is advisory (not enforced).</param>
public sealed record CustomerCreditStatusDto(
    Guid CustomerId,
    bool IsActive,
    CreditPolicyMode CreditPolicyMode,
    decimal CreditLimit,
    int? CreditTermsDays,
    decimal CurrentExposure,
    decimal? AvailableCredit);
