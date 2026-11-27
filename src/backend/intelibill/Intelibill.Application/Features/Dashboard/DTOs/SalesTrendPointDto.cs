namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record SalesTrendPointDto(DateOnly Date, decimal Amount, decimal NetAmount, decimal CancellationAmount = 0m);
