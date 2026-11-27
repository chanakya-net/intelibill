namespace Intelibill.Licensing.Application.Payments;

public interface IGatewayInboxStore
{
    Task<bool> ReceiveAsync(VerifiedGatewayEvent parsed, byte[] hash, byte[] evidence, DateTimeOffset now, CancellationToken ct);
    Task<GatewayInboxLease?> ClaimAsync(DateTimeOffset now, CancellationToken ct);
    Task<bool> LockAsync(GatewayInboxLease lease, DateTimeOffset now, CancellationToken ct);
    Task<bool> FinishAsync(GatewayInboxLease lease, GatewayInboxResolution resolution, DateTimeOffset now, CancellationToken ct);
}

public interface IGatewayInboxEvidence
{
    byte[] Protect(VerifiedGatewayEvent parsed);
}

public sealed record GatewayInboxLease(Guid InboxId, Guid Token, string Kind, string? OrderReference, string? PaymentReference,
    Guid? AccountId, Guid? AttemptId, int Candidates, int Attempts);

public enum GatewayInboxOutcome { Fulfilled, VerifiedNeedsReview, AlreadyVerified, NoAction }

public enum GatewayInboxFailure { UnknownOrder, AmbiguousOrder, AdapterUnavailable, CaptureUnproven, DifferentPayment, Busy, Unavailable }

public sealed record GatewayInboxResolution(GatewayInboxOutcome? Outcome, GatewayInboxFailure? Failure, bool DeadLetter = false)
{
    internal static GatewayInboxResolution Processed(GatewayInboxOutcome outcome) => new(outcome, null);
    internal static GatewayInboxResolution Retry(GatewayInboxFailure failure) => new(null, failure);
    internal static GatewayInboxResolution Dead(GatewayInboxFailure failure) => new(null, failure, true);
}
