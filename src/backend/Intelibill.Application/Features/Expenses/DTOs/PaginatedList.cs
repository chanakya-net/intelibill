namespace Intelibill.Application.Features.Expenses.DTOs;

public sealed record PaginatedList<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);
