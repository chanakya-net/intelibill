using ErrorOr;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed partial class Sale
{
    public SaleStatus Status { get; private set; } = SaleStatus.Posted;
    public uint Version { get; private set; }

    public ErrorOr<Success> MarkCancelled()
    {
        if (Status == SaleStatus.Cancelled)
            return Error.Conflict("SaleCancellation.AlreadyCancelled", "This sale has already been cancelled.");

        Status = SaleStatus.Cancelled;
        return Result.Success;
    }
}
