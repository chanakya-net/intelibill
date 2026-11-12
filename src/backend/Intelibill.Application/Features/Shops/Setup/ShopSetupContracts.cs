using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Shops.Setup;

public sealed record UpdateShopSetupPreferencesCommand(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId,
    int ExpectedVersion, bool ShowOnStartup, bool AcknowledgeSample);

public sealed record ShopSetupPreferencesDto(int Version, bool ShowOnStartup, DateTimeOffset? SampleReceiptAcknowledgedAt)
{
    internal static ShopSetupPreferencesDto From(ShopSetupPreferences? preferences) =>
        new(preferences?.Version ?? 0, preferences?.ShowOnStartup ?? false, preferences?.SampleReceiptAcknowledgedAt);
}

public sealed record GetShopSetupQuery(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId);
public sealed record GetShopSetupPreferencesQuery(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId);
public sealed record GetShopSetupSampleQuery(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId);

public sealed record ShopSetupStatusDto(Guid ShopId, int ContractVersion, bool CanTrade,
    IReadOnlyList<ShopSetupStepDto> Steps, IReadOnlyList<string> BlockingReasons,
    ShopSetupPermissionsDto Permissions, ShopSetupPreferencesDto Preferences);

public sealed record ShopSetupStepDto(string Id, string State, IReadOnlyList<string> Reasons);

public sealed record ShopSetupPermissionsDto(bool CanEditShop, bool CanEditCatalog, bool CanSavePreferences);

public sealed record ShopSetupSampleDto(bool IsSample, IssuedInvoiceSnapshot Document, SaleLineType LineType, DateTimeOffset GeneratedAt);
