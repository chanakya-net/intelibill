using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Shops.Setup;

public sealed record UpdateShopSetupPreferencesCommand(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId,
    int ExpectedVersion, bool ShowOnStartup, bool AcknowledgeSample);

public sealed record ShopSetupPreferencesDto(int Version, bool ShowOnStartup, DateTimeOffset? SampleReceiptAcknowledgedAt)
{
    internal static ShopSetupPreferencesDto From(ShopSetupPreferences? preferences) =>
        new(preferences?.Version ?? 0, preferences?.ShowOnStartup ?? false, preferences?.SampleReceiptAcknowledgedAt);
}
