using Intelibill.Application.Features.Services.DTOs;

namespace Intelibill.Application.Features.Services.Queries.GetServices;

public sealed record GetServicesQuery(
    Guid UserId,
    Guid ActiveShopId,
    bool IncludeInactive,
    string? Search = null);
