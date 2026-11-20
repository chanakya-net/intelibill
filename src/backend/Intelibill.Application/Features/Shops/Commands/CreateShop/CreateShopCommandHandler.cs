using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Shops.Commands.DeclareShopTaxProfile;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Commands.CreateShop;

public sealed class CreateShopCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ISupplierRepository supplierRepository,
    IAuthSessionService sessions,
    ITenantWriteScope tenantWriteScope,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IExpiryPolicyRepository expiryPolicies,
    IShopSetupPreferencesRepository setupPreferences,
    ShopSetupAccess access)
{
    [LicenceAction(LicenceAction.OperationalCapacity)]
    public async Task<ErrorOr<AccessScopeResult>> HandleAsync(CreateShopCommand command, CancellationToken ct)
    {
        if (command.Session?.SessionId != command.SessionId || !await access.IsLiveAsync(command.Session, command.UserId, ct))
            return Errors.Auth.SessionInvalidated;

        var created = await CreateAsync(command, ct);
        if (created.IsError) return created.Errors;

        // The shop remains switchable if the requesting device loses its session after commit.
        return await sessions.RescopeAsync(command.UserId, command.SessionId, created.Value, ct);
    }

    private async Task<ErrorOr<Guid>> CreateAsync(CreateShopCommand command, CancellationToken ct)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            var authorized = await AuthorizeCreationAsync(command, ct);
            if (authorized.IsError) return authorized.Errors;
            var (user, source) = authorized.Value;
            var first = command.Session!.ActiveShopId is null;
            var shop = Shop.Create(command.Name, command.Address, command.City, command.State, command.Pincode,
                command.ContactPerson, command.MobileNumber, command.GstNumber);
            var now = timeProvider.GetUtcNow();
            if (command.TaxProfile is { } profile)
            {
                var declared = DeclareShopTaxProfileCommandHandler.Declare(shop, profile, command.GstNumber, user.Id, now);
                if (declared.IsError) return declared.Errors;
            }

            await SeedAsync(shop, user, first, now, ct);
            if (source is { } lease && ShopSetupAccess.Denial(lease.CurrentDecision()) is { } denied)
                return denied;
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            committed = true;
            return shop.Id;
        }
        finally
        {
            if (!committed)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                unitOfWork.ClearChanges();
            }
        }
    }

    private async Task<ErrorOr<(User User, ShopLicenceLease? Source)>> AuthorizeCreationAsync(CreateShopCommand command, CancellationToken ct)
    {
        var session = command.Session!;
        ShopLicenceLease? source = null;
        if (session.ActiveShopId is { } sourceShop)
        {
            var licence = await access.EnterAsync(sourceShop, LicenceAction.OperationalCapacity, ct);
            if (licence.IsError) return licence.Errors;
            source = licence.Value;
        }

        if (await access.LockActorAsync(command.UserId, forShare: false, ct) is { } busy) return busy;
        if (!await access.IsLiveAsync(session, command.UserId, ct)) return Errors.Auth.SessionInvalidated;
        if (source is { } lease)
        {
            if (lease.Decision.Enforced)
            {
                var owner = await access.LockOwnerShopAsync(session, lease.ShopId, ct);
                if (owner.IsError) return owner.Errors;
            }
        }
        else if (await shopRepository.HasMembershipHistoryAsync(command.UserId, ct))
        {
            return Errors.Shop.SelectExistingShop;
        }

        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, ct);
        if (user is null) return Errors.Shop.UserNotFound;
        if (source is null && user.RequiresOperatorRecovery) return Errors.Shop.SelectExistingShop;
        if (source is { } current && ShopSetupAccess.Denial(current.CurrentDecision()) is { } denial)
            return denial;
        return (user, source);
    }

    private async Task SeedAsync(Shop shop, User user, bool first, DateTimeOffset now, CancellationToken ct)
    {
        await tenantWriteScope.EnterAsync(shop.Id, ct);
        // The initial Owner is seat one of a shop nobody else can reference before commit. The new
        // shop has no licence projection yet, and a first projection always allows at least one
        // member, so no member count applies here. The creator's seat in the source shop is
        // untouched: an Active person is counted once in each shop (MemberSeatPolicy).
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, first);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);
        await shopRepository.AddAsync(shop, ct);
        await supplierRepository.AddAsync(Supplier.CreateUnknownSystemSupplier(shop.Id), ct);
        await expiryPolicies.InitializeAsync(shop.Id, user.Id, now, ct);
        setupPreferences.Add(ShopSetupPreferences.Create(shop.Id, showOnStartup: true));
    }
}
