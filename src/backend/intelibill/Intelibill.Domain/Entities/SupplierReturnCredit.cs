using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class SupplierReturnCredit : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SupplierReturnId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset AcceptedAt { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string BusinessTimeZoneId { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string? SupplierReference { get; private set; }
    public ICollection<SupplierReturnCreditLine> Lines { get; private set; } = [];
    private SupplierReturnCredit() { }
    public static ErrorOr<SupplierReturnCredit> Create(Guid shopId, Guid returnId, Guid actor, DateTimeOffset instant, DateOnly date, string zone, string reason, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 255 || reference?.Length > 255)
            return Error.Validation("SupplierReturn.ReasonInvalid", "Reason must be 3 to 255 characters and reference at most 255.");
        return new SupplierReturnCredit { ShopId=shopId, SupplierReturnId=returnId, CreatedBy=actor, AcceptedAt=instant.ToUniversalTime(), BusinessDate=date, BusinessTimeZoneId=zone, Reason=reason.Trim(), SupplierReference=reference?.Trim() };
    }
    public ErrorOr<Success> AddLine(SupplierReturnCreditLine line)
    {
        if (line.ShopId != ShopId || line.SupplierReturnCreditId != Id || Lines.Any(l=>l.ReturnLineId==line.ReturnLineId))
            return Error.Validation("SupplierReturn.CreditLineInvalid", "Credit lines must be unique and belong to this credit.");
        Lines.Add(line); return Result.Success;
    }
}
