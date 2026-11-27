using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Audit;

public static class AdminAuditActions
{
    private static readonly Entry[] Entries =
    [
        new(BootstrapAuditAction.ProofPrepared, "proof-prepared", "succeeded"),
        new(BootstrapAuditAction.ProofReplaced, "proof-replaced", "succeeded"),
        new(BootstrapAuditAction.OwnerCreated, "owner-created", "succeeded"),
        new(BootstrapAuditAction.ProofRefused, "proof-refused", "denied"),
        new(BootstrapAuditAction.EnrollmentResumed, "enrollment-resumed", "succeeded"),
        new(BootstrapAuditAction.ResumeRefused, "resume-refused", "denied"),
        new(BootstrapAuditAction.InvitationIssued, "invitation-issued", "succeeded"),
        new(BootstrapAuditAction.InvitationAccepted, "invitation-accepted", "succeeded"),
        new(BootstrapAuditAction.InvitationRefused, "invitation-refused", "denied"),
        new(BootstrapAuditAction.InvitationSuperseded, "invitation-superseded", "succeeded"),
        new(BootstrapAuditAction.InvitationUndelivered, "invitation-undelivered", "failed"),
        new(BootstrapAuditAction.MfaEnrolled, "mfa-enrolled", "succeeded"),
        new(BootstrapAuditAction.MfaEnrollmentRefused, "mfa-enrollment-refused", "denied"),
        new(BootstrapAuditAction.SignInSucceeded, "sign-in-succeeded", "succeeded"),
        new(BootstrapAuditAction.SignInRefused, "sign-in-refused", "denied"),
        new(BootstrapAuditAction.SessionReuseRevoked, "session-reuse-revoked", "denied"),
        new(BootstrapAuditAction.DeviceSessionsInspected, "device-sessions-inspected", "succeeded"),
        new(BootstrapAuditAction.DeviceSessionRevoked, "device-session-revoked", "succeeded"),
        new(BootstrapAuditAction.DeviceSignedOut, "device-signed-out", "succeeded"),
        new(BootstrapAuditAction.DeviceSessionDenied, "device-session-denied", "denied"),
        new(BootstrapAuditAction.RecoveryCodeRedeemed, "recovery-code-redeemed", "succeeded"),
        new(BootstrapAuditAction.RecoveryRefused, "recovery-refused", "denied"),
        new(BootstrapAuditAction.RecoveryResetIssued, "recovery-reset-issued", "succeeded"),
        new(BootstrapAuditAction.RecoveryResetRedeemed, "recovery-reset-redeemed", "succeeded"),
        new(BootstrapAuditAction.RecoveryResetDenied, "recovery-reset-denied", "denied"),
        new(BootstrapAuditAction.AuditReadSucceeded, "audit-read-succeeded", "succeeded"),
        new(BootstrapAuditAction.AuditReadDenied, "audit-read-denied", "denied"),
        new(BootstrapAuditAction.InvitationManagementDenied, "invitation-management-denied", "denied"),
        new(BootstrapAuditAction.AdminCapabilitiesInspected, "admin-capabilities-inspected", "succeeded"),
        new(BootstrapAuditAction.AdminCapabilitiesChanged, "admin-capabilities-changed", "succeeded"),
        new(BootstrapAuditAction.AdminDisabled, "admin-disabled", "succeeded"),
        new(BootstrapAuditAction.AdminCapabilitiesDenied, "admin-capabilities-denied", "denied"),
    ];

    public static BootstrapAuditAction[] Readable(BootstrapAuditAction? action) => Entries
        .Where(entry => action is { } selected ? entry.Id == selected : entry.Id != BootstrapAuditAction.AuditReadSucceeded)
        .Select(entry => entry.Id).ToArray();

    public static Entry Get(BootstrapAuditAction id) => Entries.Single(entry => entry.Id == id);

    internal static Entry? Find(string name) => Entries.SingleOrDefault(entry => entry.Name == name);

    public sealed record Entry(BootstrapAuditAction Id, string Name, string Outcome);
}
