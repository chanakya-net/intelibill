using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Audit;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Audit;

internal sealed class AdminAuditStore(LicensingDbContext context, TimeProvider clock, AuditEvidenceFactory evidence)
    : IAdminAuditStore
{
    public async Task<IReadOnlyList<AdminAuditItem>> ReadAsync(AdminAuditFilters filters, Guid actor,
        CancellationToken token)
    {
        var rows = await SelectPage(filters).Select(row => new AuditRow(row.Id, row.Action, row.OccurredAt,
            row.OwnerId, row.ActorKind, row.Reason, row.CorrelationId, row.Before, row.After)).ToListAsync(token);
        await AppendAsync(actor, BootstrapAuditAction.AuditReadSucceeded, AuditReason.HistoryInspected, token);
        return rows.Select(Project).ToArray();
    }

    public Task DeniedAsync(Guid actor, BootstrapAuditAction action, AuditReason reason, CancellationToken token) =>
        AppendAsync(actor, action, reason, token);

    private IQueryable<ProviderAuditEvent> SelectPage(AdminAuditFilters filters)
    {
        var actions = AdminAuditActions.Readable(filters.Action);
        var query = context.Set<ProviderAuditEvent>().AsNoTracking().Where(row => actions.Contains(row.Action));
        if (filters.FromUtc is { } from) query = query.Where(row => row.OccurredAt >= from);
        if (filters.ToUtc is { } to) query = query.Where(row => row.OccurredAt < to);
        if (filters.BeforeTime is { } time && filters.BeforeId is { } id)
            query = query.Where(row => EF.Functions.LessThan(
                ValueTuple.Create(row.OccurredAt, row.Id), ValueTuple.Create(time, id)));

        return query.OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Id).Take(filters.PageSize + 1);
    }

    private async Task AppendAsync(Guid actor, BootstrapAuditAction action, AuditReason reason, CancellationToken token)
    {
        context.Add(evidence.Create(action, clock.GetUtcNow(), AuditActor.Administrator, reason, actor));
        await context.SaveChangesAsync(token);
    }

    private static AdminAuditItem Project(AuditRow row)
    {
        var action = AdminAuditActions.Get(row.Action);
        var kind = row.ActorKind switch
        {
            AuditActor.Operator => "operator",
            AuditActor.Unauthenticated => "unauthenticated",
            AuditActor.Enrollment => "enrollment",
            AuditActor.Administrator => "administrator",
            AuditActor.System => "system",
            _ => null,
        };
        var actorId = kind is "administrator" or "enrollment" ? row.OwnerId : null;
        var reason = row.Reason is { } code && Enum.IsDefined(code) ? code.ToString() : null;
        return new(row.Id, AdminAuditQuery.IdentityScope, action.Name, action.Outcome, row.OccurredAt,
            kind, actorId, reason, row.CorrelationId, ProjectReference(row.Before), ProjectReference(row.After));
    }

    private static AdminAuditReference? ProjectReference(AuditReference? reference) =>
        reference is null || !IsProviderReference(reference) ? null
            : new(reference.TargetKind.ToString(), reference.TargetId, reference.State.ToString(),
                reference.Version, reference.MilestoneAt?.ToUniversalTime(),
                reference.Capabilities is { } grants ? AdminCapabilityNames.ToNames(grants) : null);

    private static bool IsProviderReference(AuditReference reference) => reference switch
    {
        { TargetKind: AuditTarget.Administrator, TargetId: not null, Version: > 0, MilestoneAt: null,
            Capabilities: >= AdminCapability.None and <= (AdminCapability)31,
            State: AuditState.MfaPending or AuditState.MfaEnrolled or AuditState.Disabled } => true,
        { Capabilities: not null } => false,
        { TargetKind: AuditTarget.Bootstrap, TargetId: null, Version: null, MilestoneAt: not null,
            State: AuditState.Prepared or AuditState.Consumed } => true,
        { TargetKind: AuditTarget.Invitation, TargetId: not null, Version: not null, MilestoneAt: null,
            State: AuditState.Open or AuditState.Accepted or AuditState.Retired or AuditState.Expired } => true,
        { TargetKind: AuditTarget.Administrator, TargetId: not null, Version: null, MilestoneAt: not null,
            State: AuditState.MfaPending or AuditState.MfaEnrolled } => true,
        { TargetKind: AuditTarget.Access, TargetId: not null, Version: null, MilestoneAt: not null,
            State: AuditState.Active or AuditState.Expired or AuditState.Revoked } => true,
        { TargetKind: AuditTarget.Session, TargetId: not null, Version: > 0, MilestoneAt: not null,
            State: AuditState.Active or AuditState.Expired or AuditState.Revoked } => true,
        _ => false,
    };

    private sealed record AuditRow(Guid Id, BootstrapAuditAction Action, DateTimeOffset OccurredAt, Guid? OwnerId,
        AuditActor? ActorKind, AuditReason? Reason, Guid? CorrelationId, AuditReference? Before, AuditReference? After);
}
