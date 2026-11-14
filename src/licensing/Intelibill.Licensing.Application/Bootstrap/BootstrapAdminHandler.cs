using Intelibill.Licensing.Application.Audit;
using ErrorOr;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Bootstrap;

public sealed class BootstrapAdminHandler(IBootstrapStore store, IBootstrapSecrets secrets, TimeProvider clock,
    AuditEvidenceFactory audit)
{
    public Task<ErrorOr<EnrollmentGrant>> BootstrapAsync(
        string? proofToken, string? password, string origin, CancellationToken cancellationToken) =>
        store.WithLockedBootstrapAsync<ErrorOr<EnrollmentGrant>>((state, _) =>
            Task.FromResult(Consume(state, proofToken, password, origin)), cancellationToken);

    private ErrorOr<EnrollmentGrant> Consume(
        InitialAdminBootstrap state, string? proofToken, string? password, string origin)
    {
        var now = clock.GetUtcNow();
        if (!state.IsAvailable(now) || !string.Equals(state.AdminOrigin, origin, StringComparison.Ordinal))
            return Error.Conflict("Bootstrap.Unavailable", "Initial setup is unavailable.");

        if (!BootstrapValidation.IsProofValid(proofToken) || !BootstrapValidation.IsPasswordValid(password)
            || !secrets.MatchesProof(proofToken!, state.ProofDigest!))
        {
            store.Add(audit.Create(BootstrapAuditAction.ProofRefused, now, AuditActor.Unauthenticated,
                AuditReason.SetupProofInvalid));
            return Error.Validation("Bootstrap.InvalidSetup", "Check the setup proof and password requirements.");
        }

        var passwordHash = secrets.HashPassword(password!);
        // Work under the row lock and enforce expiry through consumption.
        now = clock.GetUtcNow();
        if (!state.IsAvailable(now))
            return Error.Conflict("Bootstrap.Unavailable", "Initial setup is unavailable.");

        var id = Guid.NewGuid();
        store.Add(new AdminIdentity(id, state.VerifiedEmail!, passwordHash, state.VerificationReference!, now));
        state.Consume(id, now);
        store.Add(audit.Create(BootstrapAuditAction.OwnerCreated, now, AuditActor.Unauthenticated,
            AuditReason.BootstrapConsumed, id,
            before: new(AuditTarget.Bootstrap, null, AuditState.Prepared, MilestoneAt: state.PreparedAt),
            after: new(AuditTarget.Bootstrap, null, AuditState.Consumed, MilestoneAt: now)));
        return IssueEnrollment(id, now);
    }

    private EnrollmentGrant IssueEnrollment(Guid id, DateTimeOffset now)
    {
        var credential = secrets.CreateEnrollment();
        var expiry = now.AddMinutes(15);
        store.Add(new BootstrapEnrollmentCredential(Guid.NewGuid(), id, credential.Digest, now, expiry));
        return new EnrollmentGrant(credential, expiry);
    }

    public Task<ErrorOr<EnrollmentGrant>> ResumeAsync(
        string? email, string? password, string origin, CancellationToken cancellationToken) =>
        store.WithLockedBootstrapAsync<ErrorOr<EnrollmentGrant>>(async (state, token) =>
        {
            var normalized = BootstrapValidation.NormalizeEmail(email);
            if (state.OwnerId is not { } ownerId || !string.Equals(state.AdminOrigin, origin, StringComparison.Ordinal))
                return Error.Unauthorized("Bootstrap.ResumeRefused", "Enrollment could not be resumed.");

            var owner = await store.LockPendingOwnerAsync(ownerId, token);
            if (owner is null) return Error.Unauthorized("Bootstrap.ResumeRefused", "Enrollment could not be resumed.");

            // Verify against the same stored hash for every well-formed password so email
            // mismatches do not expose the enrolled address through a cheap path.
            var verified = BootstrapValidation.IsPasswordValid(password) && secrets.VerifyPassword(password!, owner.PasswordHash);
            var now = clock.GetUtcNow();
            if (!verified || !string.Equals(owner.Email, normalized, StringComparison.Ordinal))
            {
                store.Add(audit.Create(BootstrapAuditAction.ResumeRefused, now, AuditActor.Unauthenticated,
                    AuditReason.EnrollmentProofInvalid, ownerId));
                return Error.Unauthorized("Bootstrap.ResumeRefused", "Enrollment could not be resumed.");
            }

            await store.RevokeEnrollmentAsync(ownerId, now, token);
            store.Add(audit.Create(BootstrapAuditAction.EnrollmentResumed, now, AuditActor.Enrollment,
                AuditReason.EnrollmentResumed, ownerId));
            return IssueEnrollment(ownerId, now);
        }, cancellationToken);
}

public sealed record EnrollmentGrant(BootstrapSecret Credential, DateTimeOffset ExpiresAt);
