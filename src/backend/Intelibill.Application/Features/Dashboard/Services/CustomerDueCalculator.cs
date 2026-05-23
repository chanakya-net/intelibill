using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Dashboard.Services;

internal static class CustomerDueCalculator
{
    internal static (CustomerDueDto? Highest, List<CustomerDueDto> TopFive) CalculateCustomerDues(
        IReadOnlyDictionary<Guid, decimal> customerBalances,
        IReadOnlyCollection<Customer> customers)
    {
        var summaries = customerBalances
            .Where(kvp => kvp.Value > 0)
            .Select(kvp =>
            {
                var customer = customers.FirstOrDefault(c => c.Id == kvp.Key);
                var displayName = customer is not null && !string.IsNullOrWhiteSpace(customer.Name)
                    ? customer.Name
                    : customer?.PhoneNumber ?? "Unknown";
                return new CustomerDueDto(kvp.Key, displayName, kvp.Value);
            })
            .OrderByDescending(d => d.OutstandingDue)
            .ToList();

        return (summaries.FirstOrDefault(), summaries.Take(5).ToList());
    }
}
