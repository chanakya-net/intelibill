namespace Intelibill.Licensing.Domain.Entities;

public enum CatalogueDraftTarget
{
    PlanVersion = 1,
    TrialPolicyVersion = 2,
}

/// <summary>Append-only evidence of one saved draft revision: who saved which revision, when and why.</summary>
public sealed class CatalogueDraftChange
{
    private CatalogueDraftChange(Guid id, CatalogueDraftTarget targetKind, Guid targetId, int revision,
        Guid actorAdminId, DateTimeOffset occurredAt, string? reason)
    {
        Id = id;
        TargetKind = targetKind;
        TargetId = targetId;
        Revision = revision;
        ActorAdminId = actorAdminId;
        OccurredAt = occurredAt;
        Reason = reason;
    }

    public Guid Id { get; private set; }
    public CatalogueDraftTarget TargetKind { get; private set; }
    public Guid TargetId { get; private set; }
    public int Revision { get; private set; }
    public Guid ActorAdminId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Reason { get; private set; }

    public static CatalogueDraftChange For(PlanVersion version) => new(Guid.CreateVersion7(version.UpdatedAt),
        CatalogueDraftTarget.PlanVersion, version.Id, version.Revision, version.UpdatedByAdminId, version.UpdatedAt,
        version.NegotiationReason);

    public static CatalogueDraftChange For(TrialPolicyVersion version) => new(Guid.CreateVersion7(version.UpdatedAt),
        CatalogueDraftTarget.TrialPolicyVersion, version.Id, version.Revision, version.UpdatedByAdminId,
        version.UpdatedAt, null);
}
