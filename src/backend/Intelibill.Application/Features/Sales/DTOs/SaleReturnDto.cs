using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record SaleReturnDto(
    Guid SaleReturnId,
    string ReturnNumber,
    DateTimeOffset ProcessedAt,
    Guid ProcessedBy,
    string? Notes,
    decimal TotalRefundAmount,
    decimal DueReductionAmount,
    decimal PayoutAmount,
    decimal TotalTaxableAmount,
    decimal TotalTaxAmount,
    IReadOnlyList<SaleReturnItemDto> Items);

public sealed record SaleReturnItemDto(
    Guid SaleReturnItemId,
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition Condition,
    decimal ApprovedRefundAmount,
    decimal TaxableAmount,
    decimal TaxAmount,
    string? Notes);
