namespace Intelibill.Licensing.Application.Payments;

public sealed record PendingPaymentLease(Guid AccountId, Guid AttemptId, Guid Token);

public interface IPendingPaymentAttemptStore
{
    Task<PendingPaymentLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct);
    Task<bool> LockAsync(PendingPaymentLease lease, DateTimeOffset now, CancellationToken ct);
    Task CompleteAsync(CancellationToken ct);
    Task FinishAsync(PendingPaymentLease lease, DateTimeOffset now, bool terminal, CancellationToken ct);
}
