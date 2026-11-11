using ErrorOr;
using FluentValidation;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Setup;

internal static class ShopSetupErrors
{
    public static Error Forbidden => Error.Forbidden("ShopSetup.Forbidden", "An active membership in the requested shop is required.");
    public static Error OwnerRequired => Error.Forbidden("ShopSetup.OwnerRequired", "Only an active owner may save shop setup preferences.");
    public static Error VersionConflict => Error.Conflict("ShopSetup.VersionConflict", "Setup preferences changed. Review the latest settings and try again.");
}

public sealed class UpdateShopSetupPreferencesCommandValidator : AbstractValidator<UpdateShopSetupPreferencesCommand>
{
    public UpdateShopSetupPreferencesCommandValidator() =>
        RuleFor(command => command.ExpectedVersion).GreaterThanOrEqualTo(0).WithErrorCode("ShopSetup.InvalidVersion");
}

public sealed class UpdateShopSetupPreferencesCommandHandler(
    IMembershipAuthorityRepository authority,
    IShopSetupPreferencesRepository repository,
    TimeProvider timeProvider)
{
    public async Task<ErrorOr<ShopSetupPreferencesDto>> HandleAsync(UpdateShopSetupPreferencesCommand command, CancellationToken ct)
    {
        if (command.ShopId != command.ActiveShopId)
            return ShopSetupErrors.Forbidden;

        await using var transaction = await authority.BeginAsync(ct);
        await authority.LockUsersAsync([command.ActorUserId], ct);
        if (!await authority.LockShopAsync(command.ShopId, ct))
            return ShopSetupErrors.Forbidden;

        var membership = await authority.LockMembershipAsync(command.ShopId, command.ActorUserId, ct);
        if (membership is not { IsActive: true, Role: ShopRole.Owner, User.IsLoginEnabled: true })
            return ShopSetupErrors.OwnerRequired;

        var saved = await repository.GetAsync(command.ShopId, ct);
        var preferences = saved ?? ShopSetupPreferences.Create(command.ShopId);
        if (command.ExpectedVersion != preferences.Version)
            return ShopSetupErrors.VersionConflict;

        var now = timeProvider.GetUtcNow();
        // Keep the first response identical to the timestamp persisted by PostgreSQL.
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        if (preferences.Change(command.ShowOnStartup, command.AcknowledgeSample, command.ActorUserId, now))
        {
            if (saved is null)
                repository.Add(preferences);
            if (!await authority.CommitAsync(ct))
                return ShopSetupErrors.VersionConflict;
        }

        return ShopSetupPreferencesDto.From(preferences);
    }
}
