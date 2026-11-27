namespace Intelibill.Application.Features.Dashboard.DTOs;

public sealed record StockShortageItemDto(string ItemName, decimal Quantity, decimal ReorderLevel, decimal Shortage);
