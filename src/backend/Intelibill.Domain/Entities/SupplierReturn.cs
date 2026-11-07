using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class SupplierReturn : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string ReturnNumber { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset ShippedAt { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string BusinessTimeZoneId { get; private set; } = string.Empty;
    public string ShopName { get; private set; } = string.Empty;
    public string SupplierName { get; private set; } = string.Empty;
    public SupplierReturnStatus Status { get; private set; }
    public uint Version { get; private set; }
    public ICollection<SupplierReturnLine> Lines { get; private set; } = [];
    public SupplierReturnCredit? Credit { get; private set; }
    private SupplierReturn() { }

    public static ErrorOr<SupplierReturn> Create(Guid shopId, Guid supplierId, string number, string reason, Guid actor, DateTimeOffset instant, DateOnly date, string zone, string shopName, string supplierName)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 255)
            return Error.Validation("SupplierReturn.ReasonInvalid", "Reason must be 3 to 255 characters.");
        return new SupplierReturn { ShopId=shopId, SupplierId=supplierId, ReturnNumber=number, Reason=reason.Trim(), CreatedBy=actor, ShippedAt=instant.ToUniversalTime(), BusinessDate=date, BusinessTimeZoneId=zone, ShopName=shopName, SupplierName=supplierName, Status=SupplierReturnStatus.PendingSupplierCredit };
    }
    public ErrorOr<Success> AddLine(SupplierReturnLine line)
    {
        if (line.ShopId != ShopId || line.SupplierReturnId != Id || Lines.Any(l => l.ReceiptValueId == line.ReceiptValueId))
            return Error.Validation("SupplierReturn.DuplicateLine", "Each receipt lot must appear exactly once in its own shop.");
        Lines.Add(line); return Result.Success;
    }
    public ErrorOr<Success> AcceptCredit(SupplierReturnCredit? credit = null)
    {
        if (Status == SupplierReturnStatus.CreditAccepted)
            return Error.Conflict("SupplierReturn.CreditAlreadyAccepted", "Supplier credit has already been accepted.");
        if (credit is not null && (credit.SupplierReturnId != Id || credit.ShopId != ShopId))
            return Error.Validation("SupplierReturn.CreditInvalid", "Credit must belong to this shipment.");
        Credit=credit;
        Status=SupplierReturnStatus.CreditAccepted; return Result.Success;
    }
}
