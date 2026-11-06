using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.DTOs;

public sealed record CustomerDto(
    Guid CustomerId,
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive,
    decimal OutstandingDue,
    decimal CreditLimit,
    int PurchaseCount = 0,
    decimal LifetimeRevenue = 0m,
    decimal CurrentMonthRevenue = 0m)
{
    public string? Gstin { get; init; }
    public string? StateCode { get; init; }

    /// <summary>How new due is judged against <see cref="CreditLimit"/>.</summary>
    public CreditPolicyMode CreditPolicyMode { get; init; }

    /// <summary>Agreed days to pay a due sale; null when no terms were agreed.</summary>
    public int? CreditTermsDays { get; init; }

    internal static CustomerDto Saved(Customer customer) =>
        new(customer.Id, customer.Name, customer.PhoneNumber, customer.Address, customer.IsActive, 0m, customer.CreditLimit)
        {
            Gstin = customer.Gstin,
            StateCode = customer.StateCode,
            CreditPolicyMode = customer.CreditPolicyMode,
            CreditTermsDays = customer.CreditTermsDays,
        };
}
