using Intelibill.Domain.ReadModels;
using Intelibill.Application.Features.Customers.DTOs;

namespace Intelibill.Application.Features.Customers.Queries.GetCustomerDirectory;

public sealed record GetCustomerDirectoryQuery(Guid ShopId, PartyDirectoryQuery Filter);
public sealed record CustomerDirectoryDto(IReadOnlyList<CustomerDto> Items, int TotalCount, int PageNumber, int PageSize, CustomerDirectorySummary Summary);
