namespace Intelibill.Domain.Entities;

/// <summary>Owner opt-in; release eligibility remains independently controlled by deployment.</summary>
public sealed class ShopTillSetting
{
    public Guid ShopId { get; private set; }
    public bool Enabled { get; private set; }
    public int Version { get; private set; }
    private ShopTillSetting() { }
    public static ShopTillSetting Create(Guid shopId, bool enabled = false) => new() { ShopId = shopId, Enabled = enabled };
    public ShopTillSettingEvent Change(bool enabled, Guid actor, string reason, DateTimeOffset now)
    {
        var previous = Enabled;
        Enabled = enabled;
        Version = checked(Version + 1);
        return ShopTillSettingEvent.Record(ShopId, previous, enabled, Version, actor, reason.Trim(), now);
    }
}
