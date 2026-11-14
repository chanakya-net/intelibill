namespace Intelibill.Licensing.Domain.Entities;

public enum BootstrapAuditAction
{
    ProofPrepared = 1,
    ProofReplaced,
    OwnerCreated,
    ProofRefused,
    EnrollmentResumed,
    ResumeRefused,
    InvitationIssued,
    InvitationAccepted,
    InvitationRefused,
    InvitationSuperseded,
    InvitationUndelivered,
    MfaEnrolled,
    MfaEnrollmentRefused,
    SignInSucceeded,
    SignInRefused,
    SessionReuseRevoked,
}

public sealed class ProviderAuditEvent(
    Guid id, BootstrapAuditAction action, DateTimeOffset occurredAt, Guid? ownerId, Guid? invitationId = null)
{
    public Guid Id { get; private set; } = id;
    public BootstrapAuditAction Action { get; private set; } = action;
    public DateTimeOffset OccurredAt { get; private set; } = occurredAt;
    /// <summary>The acting administrator, when one is known.</summary>
    public Guid? OwnerId { get; private set; } = ownerId;
    public Guid? InvitationId { get; private set; } = invitationId;
}
