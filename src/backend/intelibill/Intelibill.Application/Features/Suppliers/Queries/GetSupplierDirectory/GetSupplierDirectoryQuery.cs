using Intelibill.Domain.ReadModels;
using Intelibill.Application.Features.Suppliers.DTOs;

namespace Intelibill.Application.Features.Suppliers.Queries.GetSupplierDirectory;

public sealed record GetSupplierDirectoryQuery(Guid UserId, Guid ShopId, PartyDirectoryQuery Filter, bool IncludeSystem = true);
public sealed record SupplierDirectoryDto(IReadOnlyList<SupplierDto> Items, int TotalCount, int PageNumber, int PageSize, SupplierDirectorySummary Summary);
