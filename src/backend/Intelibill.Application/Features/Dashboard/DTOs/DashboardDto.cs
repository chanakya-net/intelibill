namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record DashboardDto(
    DateTimeOffset GeneratedAt,
    int SalesCount);
