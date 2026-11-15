using Intelibill.Licensing.Application.Audit;
using ErrorOr;
using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Bootstrap;

public sealed class PrepareBootstrapAdminHandler(IBootstrapStore store, IBootstrapSecrets secrets, TimeProvider clock,
    AuditEvidenceFactory audit)
{
    public Task<ErrorOr<BootstrapSecret>> PrepareAsync(
        string email, string reference, string origin, bool allowLoopbackHttp, CancellationToken cancellationToken)
    {
        var normalized = BootstrapValidation.NormalizeEmail(email);
        if (normalized is null || !BootstrapValidation.IsReferenceValid(reference)
            || !BootstrapValidation.IsOriginAllowed(origin, allowLoopbackHttp))
        {
            return Task.FromResult<ErrorOr<BootstrapSecret>>(
                Error.Validation("Bootstrap.InvalidPreparation", "A verified email, attestation reference and valid origin are required."));
        }

        return store.WithLockedBootstrapAsync<ErrorOr<BootstrapSecret>>((state, _) =>
        {
            if (state.ConsumedAt is not null)
                return Task.FromResult<ErrorOr<BootstrapSecret>>(Error.Conflict("Bootstrap.Consumed", "Initial setup is closed."));

            var token = secrets.CreateProof();
            var now = clock.GetUtcNow();
            var before = state.PreparedAt is { } preparedAt ? Prepared(preparedAt) : null;
            var action = before is null ? BootstrapAuditAction.ProofPrepared : BootstrapAuditAction.ProofReplaced;
            state.Prepare(token.Digest, normalized, reference, origin, now);
            store.Add(audit.Create(action, now, AuditActor.Operator, AuditReason.BootstrapPrepared,
                before: before, after: Prepared(now)));
            return Task.FromResult<ErrorOr<BootstrapSecret>>(token);
        }, cancellationToken);
    }

    private static AuditReference Prepared(DateTimeOffset at) =>
        new(AuditTarget.Bootstrap, null, AuditState.Prepared, MilestoneAt: at);
}
