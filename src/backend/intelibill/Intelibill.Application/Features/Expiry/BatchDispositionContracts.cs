namespace Intelibill.Application.Features.Expiry;

public sealed record RestorationDisposition(decimal PhysicalQuantity, decimal QuarantinedQuantity, string? ReasonCode);
public sealed record BatchDispositionRequest(string OperationKey, uint ExpectedVersion, Guid QuarantineChangeId, string Action, decimal Quantity, string Reason);
public sealed record BatchDispositionResponse(Guid ChangeId, Guid QuarantineChangeId, Guid BatchId, string Action, decimal Quantity,
    decimal PhysicalQuantity, decimal QuarantinedQuantity, decimal RootRemainingQuantity, uint Version, Guid? AdjustmentId);
