using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Audit;

public sealed class AuditEvidenceFactory
{
    private readonly Guid _correlationId = Guid.NewGuid();

    public ProviderAuditEvent Create(BootstrapAuditAction action, DateTimeOffset at, AuditActor actor,
        AuditReason reason, Guid? actorId = null, Guid? invitationId = null,
        AuditReference? before = null, AuditReference? after = null) =>
        new(action, at, _correlationId, actor, reason, actorId, invitationId, before, after);
}
