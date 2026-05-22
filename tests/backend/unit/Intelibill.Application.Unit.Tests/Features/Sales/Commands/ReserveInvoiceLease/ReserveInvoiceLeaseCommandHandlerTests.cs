using Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using Errors = Intelibill.Application.Common.Errors.Errors;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.ReserveInvoiceLease;

public class ReserveInvoiceLeaseCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IInvoiceLeaseRepository _invoiceLeaseRepository = Substitute.For<IInvoiceLeaseRepository>();

    [Fact]
    public async Task HandleAsync_WhenUserMissing_ReturnsNotFound()
    {
        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository);
        var command = new ReserveInvoiceLeaseCommand(Guid.NewGuid(), Guid.NewGuid(), "device-1", 200);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenMembershipMissing_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository);
        var command = new ReserveInvoiceLeaseCommand(actor.Id, Guid.NewGuid(), "device-1", 200);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenRoleNotOwnerOrManager_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository);
        var command = new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", 200);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.InvoiceLease.UserNotAuthorized.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_ReservesLeaseWithDefaultBlockSize()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var lease = InvoiceLease.Create(
            shop.Id,
            Guid.NewGuid(),
            "device-1",
            fiscalYearStart: 2025,
            prefix: "INV-2025-26-",
            rangeStart: 1,
            rangeEnd: 200,
            numberPadding: InvoiceLeaseDefaults.NumberPadding,
            reservedAt: new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero),
            expiresAt: new DateTimeOffset(2026, 5, 17, 0, 0, 0, TimeSpan.Zero));

        _invoiceLeaseRepository.ReserveAsync(
            shop.Id,
            "device-1",
            Arg.Any<int>(),
            Arg.Any<string>(),
            InvoiceLeaseDefaults.DefaultBlockSize,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>()).Returns(lease);

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository);
        var result = await handler.HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(lease.Id, result.Value.LeaseId);
        Assert.Equal(lease.ShopId, result.Value.ShopId);
        Assert.Equal(lease.DeviceId, result.Value.DeviceId);
        Assert.Equal(lease.RangeStart, result.Value.RangeStart);
        Assert.Equal(lease.RangeEnd, result.Value.RangeEnd);

        await _invoiceLeaseRepository.Received(1).ReserveAsync(
            shop.Id,
            "device-1",
            Arg.Any<int>(),
            Arg.Any<string>(),
            InvoiceLeaseDefaults.DefaultBlockSize,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenValid_UsesSevenDayExpiry()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var lease = InvoiceLease.Create(
            shop.Id,
            Guid.NewGuid(),
            "device-1",
            fiscalYearStart: 2025,
            prefix: "INV-2025-26-",
            rangeStart: 1,
            rangeEnd: 200,
            numberPadding: InvoiceLeaseDefaults.NumberPadding,
            reservedAt: DateTimeOffset.UtcNow,
            expiresAt: DateTimeOffset.UtcNow.AddDays(7));

        DateTimeOffset? capturedReservedAt = null;
        DateTimeOffset? capturedExpiresAt = null;

        _invoiceLeaseRepository.ReserveAsync(
            shop.Id,
            "device-1",
            Arg.Any<int>(),
            Arg.Any<string>(),
            InvoiceLeaseDefaults.DefaultBlockSize,
            Arg.Do<DateTimeOffset>(value => capturedReservedAt = value),
            Arg.Do<DateTimeOffset>(value => capturedExpiresAt = value),
            Arg.Any<CancellationToken>()).Returns(lease);

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository);
        await handler.HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null), CancellationToken.None);

        Assert.NotNull(capturedReservedAt);
        Assert.NotNull(capturedExpiresAt);
        Assert.Equal(TimeSpan.FromDays(InvoiceLeaseDefaults.LeaseDurationDays), capturedExpiresAt - capturedReservedAt);
    }
}
