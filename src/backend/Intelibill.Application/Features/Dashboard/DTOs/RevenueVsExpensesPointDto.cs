namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record RevenueVsExpensesPointDto(DateOnly Date, decimal Revenue, decimal Expenses);
