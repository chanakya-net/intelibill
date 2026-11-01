namespace Intelibill.Application.Features.PurchaseReceipts.Commands.CorrectReceiptCost;

/// <summary>
/// Corrects a purchase receipt's total cost, entered against the receipt version the operator was shown.
/// </summary>
public sealed record CorrectReceiptCostCommand(
    Guid ReceiptValueId,
    Guid ActorUserId,
    Guid ActiveShopId,
    uint ExpectedVersion,
    decimal RevisedTotalCost,
    string Reason,
    string? OperationKey);
