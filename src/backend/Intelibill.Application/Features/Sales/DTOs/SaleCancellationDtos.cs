using Intelibill.Application.Features.Tills;
using FluentValidation;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record CancelSaleRequest(string OperationKey, uint ExpectedVersion, string Reason, bool GoodsRecoveredConfirmed, bool CashRefundConfirmed, TillPostingContext? Till = null);
public sealed class CancelSaleRequestValidator : AbstractValidator<CancelSaleRequest>
{
    public CancelSaleRequestValidator()
    {
        RuleFor(x => x.OperationKey).Must(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= 128).WithMessage("Operation key must be 1 to 128 characters.");
        RuleFor(x => x.ExpectedVersion).NotEqual(0u);
        RuleFor(x => x.Reason).Must(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length is >= 3 and <= 255).WithMessage("Reason must be 3 to 255 characters.");
        RuleFor(x => x.GoodsRecoveredConfirmed).Equal(true).WithMessage("Confirm all goods were recovered in restockable condition.");
    }
}
public sealed record SaleCancellationReasonDto(string Code, string Message);
public sealed record SaleCancellationPreviewLineDto(Guid SaleItemId, Guid? ItemId, Guid? InventoryBatchId, decimal Quantity, string Description, string Uqc, decimal PhysicalRestorationQuantity = 0, decimal QuarantinedQuantity = 0, string? DispositionReasonCode = null);
public sealed record SaleCancellationPreviewDto(Guid SaleId, string InvoiceNumber, IssuedInvoiceSnapshot? Document, SaleStatus Status, uint ExpectedVersion, bool Eligible, bool PostingEnabled, DateOnly BusinessDate,
    IReadOnlyList<SaleCancellationReasonDto> Reasons, IReadOnlyList<SaleCancellationPreviewLineDto> Lines, decimal TotalAmount, decimal DueReductionAmount, decimal CashRefundAmount, decimal ExternalRefundAmount, decimal CreditRestorationAmount, bool RequiresGoodsConfirmation, bool RequiresCashRefundConfirmation);
public sealed record SaleCancellationLineDto(Guid Id, Guid SaleItemId, Guid ItemId, Guid InventoryBatchId, Guid StockTransactionId, decimal Quantity, string Description, string Uqc, decimal OriginalCostPrice, decimal RevenueAmount, decimal RecordedCostAmount, Guid? QuarantineChangeId = null, decimal QuarantinedQuantity = 0, string? DispositionReasonCode = null);
public sealed record SaleCancellationDto(Guid Id, Guid SaleId, string Reference, string Reason, Guid ActorUserId, DateTimeOffset CancelledAt, DateOnly BusinessDate, string BusinessTimeZoneId, string PolicyVersion,
    string OriginalInvoiceNumber, DateOnly OriginalBusinessDate, decimal TotalAmount, decimal RevenueAmount, decimal TaxAmount, decimal DueReductionAmount, decimal CashRefundAmount, Guid? CustomerLedgerEntryId,
    InvoiceSeller Seller, InvoiceBuyer Buyer, bool GoodsRecoveredConfirmed, bool CashRefundConfirmed, IReadOnlyList<SaleCancellationLineDto> Lines)
{
    public static SaleCancellationDto From(SaleCancellation x) => new(x.Id, x.SaleId, x.ReferenceNumber, x.Reason, x.ActorUserId, x.CancelledAt, x.BusinessDate, x.BusinessTimeZoneId, x.PolicyVersion, x.OriginalInvoiceNumber, x.OriginalBusinessDate,
        x.TotalAmount, x.RevenueAmount, x.TaxAmount, x.DueReductionAmount, x.CashRefundAmount, x.CustomerLedgerEntryId, x.Seller, x.Buyer, x.GoodsRecoveredConfirmed, x.CashRefundConfirmed,
        x.Lines.Select(l => new SaleCancellationLineDto(l.Id, l.SaleItemId, l.ItemId, l.InventoryBatchId, l.StockTransactionId, l.Quantity, l.Description, l.Uqc, l.OriginalCostPrice, l.RevenueAmount, l.RecordedCostAmount, l.QuarantineChangeId, l.QuarantinedQuantity, l.DispositionReasonCode)).ToArray());
}
