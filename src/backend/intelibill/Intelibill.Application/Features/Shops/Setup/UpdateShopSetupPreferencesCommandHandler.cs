using Intelibill.Application.Common.Licensing;
using ErrorOr;
using FluentValidation;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Shops.Setup;

internal static class ShopSetupErrors
{
    public static Error Forbidden => Errors.ShopSetup.Forbidden;
    public static Error OwnerRequired => Errors.ShopSetup.OwnerRequired;
    public static Error VersionConflict => Errors.ShopSetup.VersionConflict;
}

public sealed class UpdateShopSetupPreferencesCommandValidator : AbstractValidator<UpdateShopSetupPreferencesCommand>
{
    public UpdateShopSetupPreferencesCommandValidator() =>
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0).WithErrorCode("ShopSetup.InvalidVersion");
}

public sealed class UpdateShopSetupPreferencesCommandHandler(
    ShopSetupAccess access,
    IShopSetupPreferencesRepository repository,
    TimeProvider timeProvider)
{
    [LicenceAction(LicenceAction.BasicShopSetup)]
    public async Task<ErrorOr<ShopSetupPreferencesDto>> HandleAsync(UpdateShopSetupPreferencesCommand command, CancellationToken ct)
    {
        if (command.ShopId != command.ActiveShopId || command.Session?.ActiveShopId != command.ShopId)
            return ShopSetupErrors.Forbidden;
        var result = await access.WriteAsync<ShopSetupPreferencesDto>(command.Session, command.ActorUserId, command.ShopId,
            LicenceAction.BasicShopSetup, (_, lease, token) => WriteAsync(command, lease, token), ct);
        return result.IsError && result.FirstError.Code is "Shop.MembershipNotFound" or "Shop.UserIsNotOwner"
            ? ShopSetupErrors.OwnerRequired : result;
    }

    private async Task<ErrorOr<ShopSetupPreferencesDto>> WriteAsync(UpdateShopSetupPreferencesCommand command,
        ShopLicenceLease lease, CancellationToken ct)
    {
        var saved = await repository.GetAsync(command.ShopId, ct);
        if (ShopSetupAccess.Denial(lease.CurrentDecision(), allowSetup: true) is { } denial) return denial;

        var preferences = saved ?? ShopSetupPreferences.Create(command.ShopId);
        if (command.ExpectedVersion != preferences.Version)
            return ShopSetupErrors.VersionConflict;

        var now = timeProvider.GetUtcNow();
        // Keep the first response identical to the timestamp persisted by PostgreSQL.
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        if (preferences.Change(command.ShowOnStartup, command.AcknowledgeSample, command.ActorUserId, now) && saved is null)
            repository.Add(preferences);
        return ShopSetupPreferencesDto.From(preferences, canSave: true);
    }
}
