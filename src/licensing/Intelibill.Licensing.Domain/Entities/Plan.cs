namespace Intelibill.Licensing.Domain.Entities;

public enum PlanKind
{
    SingleShop = 1,
    MultiShop = 2,
    Custom = 3,
}

/// <summary>Draft, published or retired. Only drafts exist until publication is built; a draft is never offered.</summary>
public enum CatalogueVersionStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

/// <summary>
/// A provider-owned commercial plan identity. Its kind is fixed at creation; every commercial fact lives on
/// its immutable-once-published <see cref="PlanVersion"/> rows.
/// </summary>
public sealed class Plan
{
    private Plan(Guid id, PlanKind kind, DateTimeOffset createdAt, Guid createdByAdminId)
    {
        Id = id;
        Kind = kind;
        CreatedAt = createdAt;
        CreatedByAdminId = createdByAdminId;
    }

    public Guid Id { get; private set; }
    public PlanKind Kind { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAdminId { get; private set; }

    public static Plan Create(Guid id, PlanKind kind, Guid adminId, DateTimeOffset now) => new(id, kind, now, adminId);
}
