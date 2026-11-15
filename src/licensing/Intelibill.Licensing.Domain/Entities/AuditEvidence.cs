namespace Intelibill.Licensing.Domain.Entities;

public enum AuditActor
{
    Operator = 1,
    Unauthenticated = 2,
    Enrollment = 3,
    Administrator = 4,
    System = 5,
}

public enum AuditReason
{
    BootstrapPrepared = 1,
    BootstrapConsumed = 2,
    SetupProofInvalid = 3,
    EnrollmentResumed = 4,
    EnrollmentProofInvalid = 5,
    InvitationIssued = 6,
    InvitationAccepted = 7,
    InvitationIdentityMismatch = 8,
    InvitationSuperseded = 9,
    InvitationDeliveryFailed = 10,
    AuthenticatorEnrolled = 11,
    AuthenticatorProofInvalid = 12,
    CredentialsVerified = 13,
    CredentialsRefused = 14,
    RefreshCredentialReused = 15,
    HistoryInspected = 16,
    CapabilityRequired = 17,
    ScopeUnavailable = 18,
    RecentMfaRequired = 19,
    RequestRejected = 20,
    RequestThrottled = 21,
    DeviceSessionsInspected = 22,
    DeviceUnused = 23,
    DeviceUnrecognized = 24,
    SignedOut = 25,
    RecoveryCodeAccepted = 26,
    RecoveryProofInvalid = 27,
    RecoveryResetReviewed = 28,
    RecoveryResetAccepted = 29,
    LastRecoverableOwner = 30,
    RecoveryReset = 31,
}

public enum AuditTarget
{
    Bootstrap = 1,
    Invitation = 2,
    Administrator = 3,
    Access = 4,
    Session = 5,
}

public enum AuditState
{
    Prepared = 1,
    Consumed = 2,
    Open = 3,
    Accepted = 4,
    Retired = 5,
    MfaPending = 6,
    MfaEnrolled = 7,
    Active = 8,
    Revoked = 9,
    Expired = 10,
}

public sealed record AuditReference(AuditTarget TargetKind, Guid? TargetId, AuditState State,
    uint? Version = null, DateTimeOffset? MilestoneAt = null);
