namespace Intelibill.Domain.Enums;

public enum OperationReceiptStatus
{
    /// <summary>Reserved inside an open transaction; never visible once committed.</summary>
    Pending = 1,
    Completed = 2,
}
