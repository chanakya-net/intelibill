using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Audit;

public static class AdminAuditActions
{
    private static readonly Entry[] Entries =
    [
        new(BootstrapAuditAction.ProofPrepared, "proof-prepared", "succeeded", "operator"),
        new(BootstrapAuditAction.ProofReplaced, "proof-replaced", "succeeded", "operator"),
        new(BootstrapAuditAction.OwnerCreated, "owner-created", "succeeded", "unauthenticated"),
        new(BootstrapAuditAction.ProofRefused, "proof-refused", "denied", "unauthenticated"),
        new(BootstrapAuditAction.EnrollmentResumed, "enrollment-resumed", "succeeded", "enrollment"),
        new(BootstrapAuditAction.ResumeRefused, "resume-refused", "denied", "unauthenticated"),
        new(BootstrapAuditAction.InvitationIssued, "invitation-issued", "succeeded", "administrator"),
        new(BootstrapAuditAction.InvitationAccepted, "invitation-accepted", "succeeded", "unauthenticated"),
        new(BootstrapAuditAction.InvitationRefused, "invitation-refused", "denied", "unauthenticated"),
        new(BootstrapAuditAction.InvitationSuperseded, "invitation-superseded", "succeeded", "administrator"),
        new(BootstrapAuditAction.InvitationUndelivered, "invitation-undelivered", "failed", "administrator"),
        new(BootstrapAuditAction.MfaEnrolled, "mfa-enrolled", "succeeded", "enrollment"),
        new(BootstrapAuditAction.MfaEnrollmentRefused, "mfa-enrollment-refused", "denied", "enrollment"),
        new(BootstrapAuditAction.SignInSucceeded, "sign-in-succeeded", "succeeded", "administrator"),
        new(BootstrapAuditAction.SignInRefused, "sign-in-refused", "denied", "unauthenticated"),
        new(BootstrapAuditAction.SessionReuseRevoked, "session-reuse-revoked", "denied", "system"),
        new(BootstrapAuditAction.DeviceSessionsInspected, "device-sessions-inspected", "succeeded", "administrator"),
        new(BootstrapAuditAction.DeviceSessionRevoked, "device-session-revoked", "succeeded", "administrator"),
        new(BootstrapAuditAction.DeviceSignedOut, "device-signed-out", "succeeded", "administrator"),
        new(BootstrapAuditAction.DeviceSessionDenied, "device-session-denied", "denied", "administrator"),
        new(BootstrapAuditAction.RecoveryCodeRedeemed, "recovery-code-redeemed", "succeeded", "enrollment"),
        new(BootstrapAuditAction.RecoveryRefused, "recovery-refused", "denied", "unauthenticated"),
        new(BootstrapAuditAction.RecoveryResetIssued, "recovery-reset-issued", "succeeded", "administrator"),
        new(BootstrapAuditAction.RecoveryResetRedeemed, "recovery-reset-redeemed", "succeeded", "enrollment"),
        new(BootstrapAuditAction.RecoveryResetDenied, "recovery-reset-denied", "denied", "administrator"),
        new(BootstrapAuditAction.AuditReadSucceeded, "audit-read-succeeded", "succeeded", "administrator"),
        new(BootstrapAuditAction.AuditReadDenied, "audit-read-denied", "denied", "administrator"),
        new(BootstrapAuditAction.InvitationManagementDenied, "invitation-management-denied", "denied", "administrator"),
        new(BootstrapAuditAction.AdminCapabilitiesInspected, "admin-capabilities-inspected", "succeeded", "administrator"),
        new(BootstrapAuditAction.AdminCapabilitiesChanged, "admin-capabilities-changed", "succeeded", "administrator"),
        new(BootstrapAuditAction.AdminDisabled, "admin-disabled", "succeeded", "administrator"),
        new(BootstrapAuditAction.AdminCapabilitiesDenied, "admin-capabilities-denied", "denied", "administrator"),
    ];

    public static BootstrapAuditAction[] Readable(BootstrapAuditAction? action) => Entries
        .Where(entry => action is { } selected ? entry.Id == selected : entry.Id != BootstrapAuditAction.AuditReadSucceeded)
        .Select(entry => entry.Id).ToArray();

    public static Entry Get(BootstrapAuditAction id) => Entries.Single(entry => entry.Id == id);

    internal static Entry? Find(string name) => Entries.SingleOrDefault(entry => entry.Name == name);

    public sealed record Entry(BootstrapAuditAction Id, string Name, string Outcome, string LegacyActor);
}
