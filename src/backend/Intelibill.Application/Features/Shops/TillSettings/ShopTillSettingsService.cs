using ErrorOr;
using Intelibill.Application.Common.Normalization;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using TillErrors = Intelibill.Application.Common.Errors.Errors.Till;

namespace Intelibill.Application.Features.Shops.TillSettings;

public sealed record ShopTillSettingsDto(Guid ShopId, bool Available, bool Enabled, int Version,
    int OpenSessionCount, int AwaitingReviewCount, int PendingOfflineCount);
public sealed record GetShopTillSettingsQuery(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId);
public sealed record UpdateShopTillSettingsCommand(Guid ShopId, Guid ActorUserId, Guid? ActiveShopId,
    bool Enabled, int ExpectedVersion, string Reason, bool Confirmed);

public sealed class ShopTillSettingsService(ITillRepository tills, IShopRepository shops,
    IMembershipAuthorityRepository authority, ITillSessionPolicy policy, TimeProvider clock)
{
    public async Task<ErrorOr<ShopTillSettingsDto>> GetAsync(GetShopTillSettingsQuery query, CancellationToken ct)
    {
        if (query.ShopId != query.ActiveShopId ||
            await shops.GetActiveRoleAsync(query.ActorUserId, query.ShopId, ct) is not (ShopRole.Owner or ShopRole.Manager))
            return TillErrors.Forbidden;
        return await ReadAsync(query.ShopId, ct);
    }

    public async Task<ErrorOr<ShopTillSettingsDto>> UpdateAsync(UpdateShopTillSettingsCommand command, CancellationToken ct)
    {
        if (command.ShopId != command.ActiveShopId) return TillErrors.Forbidden;
        if (command.ExpectedVersion < 0 || !command.Confirmed || string.IsNullOrWhiteSpace(command.Reason) ||
            command.Reason.Trim().Length > 500 || !StorableText.IsStorable(command.Reason))
            return Error.Validation("Till.SettingsConfirmationRequired", "Confirm the change and provide a reason of 1 to 500 characters.");
        await using var transaction = await authority.BeginAsync(ct);
        // Follow the authority protocol. The catalog shop lock is also held by opening and
        // conflicts with transaction-held source gate reads, including disabled legacy writers.
        // SHARE blocks account/authority changes but remains compatible with source FK reads.
        var actorLock = await tills.LockSettingsActorAsync(command.ActorUserId, ct);
        if (actorLock != RowLockOutcome.Acquired) return actorLock == RowLockOutcome.TimedOut ? TillErrors.Busy : TillErrors.Forbidden;
        var locked = await tills.LockCatalogAsync(command.ShopId, ct);
        if (locked != RowLockOutcome.Acquired) return locked == RowLockOutcome.TimedOut ? TillErrors.Busy : TillErrors.NotFound;
        var membership = await authority.LockMembershipAsync(command.ShopId, command.ActorUserId, ct);
        var actor = await authority.FindUserAsync(command.ActorUserId, ct);
        if (membership is not { IsActive: true, Role: ShopRole.Owner } || actor is not { IsLoginEnabled: true }) return TillErrors.Forbidden;
        var settings = await tills.GetSettingsAsync(command.ShopId, ct) ?? ShopTillSetting.Create(command.ShopId);
        if (settings.Version != command.ExpectedVersion) return Error.Conflict("Till.SettingsVersionConflict", "Till setup changed. Refresh its current state before trying again.");
        if (!policy.IsAvailable(command.ShopId)) return Error.Conflict("Till.SettingsUnavailable", "Till tracking is not available for this shop's deployment.");
        var blockers = await tills.GetSettingsBlockersAsync(command.ShopId, ct);
        if (!command.Enabled && (blockers.Open != 0 || blockers.AwaitingReview != 0 || blockers.PendingOffline != 0))
            return Error.Conflict("Till.SettingsBlocked", "Close and review all sessions and resolve known offline submissions before disabling till tracking.");
        if (settings.Enabled != command.Enabled)
        {
            var audit = settings.Change(command.Enabled, command.ActorUserId, command.Reason, clock.GetUtcNow());
            await tills.SaveSettingsAsync(settings, ct);
            await tills.AddSettingsEventAsync(audit, ct);
            if (!await authority.CommitAsync(ct)) return Error.Conflict("Till.SettingsVersionConflict", "Till setup changed. Refresh its current state before trying again.");
        }
        return new ShopTillSettingsDto(command.ShopId, policy.IsAvailable(command.ShopId), settings.Enabled, settings.Version,
            blockers.Open, blockers.AwaitingReview, blockers.PendingOffline);
    }

    private async Task<ShopTillSettingsDto> ReadAsync(Guid shopId, CancellationToken ct)
    {
        var settings = await tills.GetSettingsAsync(shopId, ct);
        var blockers = await tills.GetSettingsBlockersAsync(shopId, ct);
        return new(shopId, policy.IsAvailable(shopId), settings?.Enabled ?? false, settings?.Version ?? 0,
            blockers.Open, blockers.AwaitingReview, blockers.PendingOffline);
    }
}

public sealed class GetShopTillSettingsQueryHandler(ShopTillSettingsService service)
{
    public Task<ErrorOr<ShopTillSettingsDto>> HandleAsync(GetShopTillSettingsQuery query, CancellationToken ct) => service.GetAsync(query, ct);
}
public sealed class UpdateShopTillSettingsCommandHandler(ShopTillSettingsService service)
{
    public Task<ErrorOr<ShopTillSettingsDto>> HandleAsync(UpdateShopTillSettingsCommand command, CancellationToken ct) => service.UpdateAsync(command, ct);
}
