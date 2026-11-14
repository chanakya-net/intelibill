using Intelibill.Licensing.Application.Audit;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;

namespace Intelibill.Licensing.Infrastructure.AdminIdentities;

internal static class AdminSessionRevocation
{
    internal static async Task StageAsync(LicensingDbContext context, AuditEvidenceFactory audit, AdminSession session,
        DateTimeOffset now, BootstrapAuditAction action, AuditReason reason, CancellationToken token)
    {
        if (session.RevokedAt is not null) return;
        var before = new AuditReference(AuditTarget.Session, session.Id, session.IsLiveAt(now) ? AuditState.Active : AuditState.Expired,
            session.Version, session.CreatedAt);
        session.Revoke(now);
        await context.SaveChangesAsync(token);
        context.Add(audit.Create(action, now, AuditActor.Administrator, reason, session.AdminId,
            before: before, after: new(AuditTarget.Session, session.Id, AuditState.Revoked, session.Version, session.RevokedAt)));
    }
}
