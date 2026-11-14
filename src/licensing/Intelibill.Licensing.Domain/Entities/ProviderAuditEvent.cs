namespace Intelibill.Licensing.Domain.Entities;

public enum BootstrapAuditAction
{
    ProofPrepared = 1,
    ProofReplaced = 2,
    OwnerCreated = 3,
    ProofRefused = 4,
    EnrollmentResumed = 5,
    ResumeRefused = 6,
    InvitationIssued = 7,
    InvitationAccepted = 8,
    InvitationRefused = 9,
    InvitationSuperseded = 10,
    InvitationUndelivered = 11,
    MfaEnrolled = 12,
    MfaEnrollmentRefused = 13,
    SignInSucceeded = 14,
    SignInRefused = 15,
    SessionReuseRevoked = 16,
    DeviceSessionsInspected = 17,
    DeviceSessionRevoked = 18,
    DeviceSignedOut = 19,
    DeviceSessionDenied = 20,
    AuditReadSucceeded = 101,
    AuditReadDenied = 102,
    InvitationManagementDenied = 103,
}

public sealed class ProviderAuditEvent(
    Guid id, BootstrapAuditAction action, DateTimeOffset occurredAt, Guid? ownerId, Guid? invitationId = null)
{
    public Guid Id { get; private set; } = id;
    public BootstrapAuditAction Action { get; private set; } = action;
    public DateTimeOffset OccurredAt { get; private set; } = occurredAt;
    /// <summary>The related administrator. ActorKind distinguishes actors from subjects.</summary>
    public Guid? OwnerId { get; private set; } = ownerId;
    public Guid? InvitationId { get; private set; } = invitationId;
    public AuditActor? ActorKind { get; private set; }
    public AuditReason? Reason { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public AuditReference? Before { get; private set; }
    public AuditReference? After { get; private set; }

    public ProviderAuditEvent(BootstrapAuditAction action, DateTimeOffset occurredAt, Guid correlationId,
        AuditActor actorKind, AuditReason reason, Guid? actorId = null, Guid? invitationId = null,
        AuditReference? before = null, AuditReference? after = null)
        : this(Guid.NewGuid(), action, occurredAt, actorId, invitationId)
    {
        ActorKind = actorKind;
        Reason = reason;
        CorrelationId = correlationId;
        Before = before;
        After = after;
    }
}
