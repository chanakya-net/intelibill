using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Shops.Setup;

public sealed record UpdateShopSetupPreferencesCommand(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId,
    int ExpectedVersion, bool ShowOnStartup, bool AcknowledgeSample)
{
    public SessionClaims? Session { get; init; }
}

public sealed record ShopSetupPreferencesDto(int Version, bool ShowOnStartup, DateTimeOffset? SampleReceiptAcknowledgedAt)
{
    public bool CanSavePreferences { get; init; }

    internal static ShopSetupPreferencesDto From(ShopSetupPreferences? preferences, bool canSave) =>
        new(preferences?.Version ?? 0, preferences?.ShowOnStartup ?? false, preferences?.SampleReceiptAcknowledgedAt)
        { CanSavePreferences = canSave };
}

public sealed record GetShopSetupQuery(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId)
{
    public SessionClaims? Session { get; init; }
}

public sealed record GetShopSetupPreferencesQuery(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId)
{
    public SessionClaims? Session { get; init; }
}

public sealed record GetShopSetupSampleQuery(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId)
{
    public SessionClaims? Session { get; init; }
}

public sealed record ShopSetupStatusDto(Guid ShopId, int ContractVersion, bool CanTrade,
    IReadOnlyList<ShopSetupStepDto> Steps, IReadOnlyList<string> BlockingReasons,
    ShopSetupPermissionsDto Permissions, ShopSetupPreferencesDto Preferences)
{
    public ShopSetupAccessDto? SetupAccess { get; init; }
}

public sealed record ShopSetupStepDto(string Id, string State, IReadOnlyList<string> Reasons);

public sealed record ShopSetupPermissionsDto(bool CanEditShop, bool CanEditCatalog, bool CanSavePreferences);

public sealed record ShopSetupSampleDto(bool IsSample, IssuedInvoiceSnapshot Document, SaleLineType LineType, DateTimeOffset GeneratedAt);
