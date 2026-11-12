namespace Intelibill.Domain.Entities;

public sealed class ShopSetupPreferences
{
    public Guid ShopId { get; private set; }
    public int Version { get; private set; }
    public bool ShowOnStartup { get; private set; }
    public DateTimeOffset? SampleReceiptAcknowledgedAt { get; private set; }
    public Guid? SampleReceiptAcknowledgedByUserId { get; private set; }

    private ShopSetupPreferences() { }

    public static ShopSetupPreferences Create(Guid shopId, bool showOnStartup = false) =>
        new() { ShopId = shopId, ShowOnStartup = showOnStartup };

    public bool Change(bool showOnStartup, bool acknowledgeSample, Guid actor, DateTimeOffset now)
    {
        var firstAcknowledgement = acknowledgeSample && SampleReceiptAcknowledgedAt is null;
        if (ShowOnStartup == showOnStartup && !firstAcknowledgement)
            return false;

        Version = checked(Version + 1);
        ShowOnStartup = showOnStartup;
        if (firstAcknowledgement)
        {
            SampleReceiptAcknowledgedAt = now;
            SampleReceiptAcknowledgedByUserId = actor;
        }
        return true;
    }
}
