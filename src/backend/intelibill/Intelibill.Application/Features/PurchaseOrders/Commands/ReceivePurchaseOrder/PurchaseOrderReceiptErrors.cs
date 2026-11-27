using ErrorOr;

namespace Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

public static class PurchaseOrderReceiptErrors
{
    public static Error ForLine(Error error, Guid? clientLineId, int lineIndex, string field)
    {
        var metadata = error.Metadata is null ? new Dictionary<string, object>() : new Dictionary<string, object>(error.Metadata);
        if (clientLineId.HasValue) metadata["clientLineId"] = clientLineId.Value;
        metadata["lineIndex"] = lineIndex;
        metadata["field"] = field;
        return Error.Custom(error.NumericType, error.Code, error.Description, metadata);
    }
}
