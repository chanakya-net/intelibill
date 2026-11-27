using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Refunds;

public sealed record StoredRefundRequest(RefundRequestV1 Request, Guid ActorId, string RequestHash);

public sealed record RefundablePayment(
    Guid ConfirmationId,
    Guid OrderId,
    Guid? AttemptId,
    Guid? ManualReceiptId,
    long AmountPaise,
    string Currency,
    string ProviderId,
    string ProviderAccountId,
    string Environment,
    string ConfigurationVersion);

public sealed record RefundIntakeItem(
    Guid RefundRequestId,
    Guid AccountId,
    Guid OrderId,
    Guid ConfirmationId,
    Guid ActorId,
    string PaymentSource,
    string Status,
    long AmountPaise,
    long PaymentAmountPaise,
    string Currency,
    string Reason,
    DateTimeOffset RequestedAt,
    string ProviderId,
    string ProviderAccountId,
    string Environment,
    string ConfigurationVersion)
{
    public string? RefundReference { get; init; }
    public string? ObservationReason { get; init; }
    public DateTimeOffset? LastCheckedAt { get; init; }
    public long? ApprovedAmountPaise { get; init; }
    public string? ProviderState { get; init; }
    public string? AccessDisposition { get; init; }
    public override string ToString() => "[REDACTED]";
}

public sealed record RefundIntakePage(Guid AccountId, RefundIntakeItem[] Items, Guid? NextCursor)
{
    public int SchemaVersion { get; init; } = 1;
}

public interface IRefundRequestStore
{
    Task<bool> ScheduleLookupAsync(Guid account, Guid request, DateTimeOffset at, string? simulated, CancellationToken ct);
    Task<StoredRefundRequest?> FindAsync(Guid account, Guid operationKey, CancellationToken ct);
    Task<RefundablePayment?> PaymentAsync(Guid account, Guid order, Guid confirmation, CancellationToken ct);
    Task SaveAsync(RefundRequestV1 request, RefundablePayment payment, Guid actor, Guid operationKey,
        string requestHash, Guid replay, CancellationToken ct);
    Task<RefundRequestPageV1> PageAsync(Guid account, Guid? after, CancellationToken ct);
    Task<RefundIntakePage> IntakeAsync(Guid account, Guid? after, CancellationToken ct, Guid? request = null);
}
