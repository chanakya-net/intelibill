namespace Intelibill.Licensing.Domain.Entities;

public enum BootstrapAuditAction
{
    ProofPrepared = 1,
    ProofReplaced,
    OwnerCreated,
    ProofRefused,
    EnrollmentResumed,
    ResumeRefused,
}

public sealed class ProviderAuditEvent(
    Guid id, BootstrapAuditAction action, DateTimeOffset occurredAt, Guid? ownerId)
{
    public Guid Id { get; private set; } = id;
    public BootstrapAuditAction Action { get; private set; } = action;
    public DateTimeOffset OccurredAt { get; private set; } = occurredAt;
    public Guid? OwnerId { get; private set; } = ownerId;
}
