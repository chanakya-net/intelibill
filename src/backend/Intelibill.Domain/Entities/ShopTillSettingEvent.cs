using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class ShopTillSettingEvent : BaseEntity
{
    public Guid ShopId { get; private set; }
    public bool PreviousEnabled { get; private set; }
    public bool Enabled { get; private set; }
    public int Version { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    private ShopTillSettingEvent() { }
    internal static ShopTillSettingEvent Record(Guid shopId, bool previous, bool enabled, int version, Guid actor, string reason, DateTimeOffset now) =>
        new() { ShopId = shopId, PreviousEnabled = previous, Enabled = enabled, Version = version, ActorUserId = actor, Reason = reason, CreatedAt = now };
}
