namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record ProfitTrendPointDto(DateOnly Date, decimal ProfitBeforeTax, decimal ProfitAfterTax);
