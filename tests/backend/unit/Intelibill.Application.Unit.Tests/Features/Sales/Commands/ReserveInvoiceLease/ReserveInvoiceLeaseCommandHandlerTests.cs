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
        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository, TimeProvider.System);
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

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository, TimeProvider.System);
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

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository, TimeProvider.System);
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
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>()).Returns(lease);

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository, TimeProvider.System);
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
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reserve_RecordsReserverAndSession()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        var sessionId = Guid.NewGuid();
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
            expiresAt: DateTimeOffset.UtcNow.AddDays(7),
            reservedByUserId: actor.Id,
            reservedBySessionId: sessionId);
        _invoiceLeaseRepository.ReserveAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>()).Returns(lease);

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository, TimeProvider.System);
        var result = await handler.HandleAsync(
            new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null, sessionId),
            CancellationToken.None);

        Assert.False(result.IsError);
        await _invoiceLeaseRepository.Received(1).ReserveAsync(
            shop.Id,
            "device-1",
            Arg.Any<int>(),
            Arg.Any<string>(),
            InvoiceLeaseDefaults.DefaultBlockSize,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            actor.Id,
            sessionId,
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
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>()).Returns(lease);

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository, new FixedClock(new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero)));
        await handler.HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null), CancellationToken.None);

        Assert.NotNull(capturedReservedAt);
        Assert.NotNull(capturedExpiresAt);
        Assert.Equal(TimeSpan.FromDays(InvoiceLeaseDefaults.LeaseDurationDays), capturedExpiresAt - capturedReservedAt);
    }

    [Theory]
    // Reserved four days before the fiscal year ends: the lease stops at 1 April 00:00 UTC, so it never straddles two years.
    [InlineData("2027-03-28T10:00:00+00:00", "2027-04-01T00:00:00+00:00", 2026)]
    [InlineData("2027-03-31T23:59:00+00:00", "2027-04-01T00:00:00+00:00", 2026)]
    // Well inside the year the usual seven days apply.
    [InlineData("2027-03-10T10:00:00+00:00", "2027-03-17T10:00:00+00:00", 2026)]
    [InlineData("2027-04-01T00:00:00+00:00", "2027-04-08T00:00:00+00:00", 2027)]
    public async Task Reserve_ExpiryCappedAtFiscalYearEnd(string reservedAt, string expectedExpiry, int fiscalYearStart)
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        var now = DateTimeOffset.Parse(reservedAt, System.Globalization.CultureInfo.InvariantCulture);
        var lease = InvoiceLease.Create(shop.Id, Guid.NewGuid(), "device-1", fiscalYearStart, "INV-", 1, 200, 6, now, now.AddDays(1));
        int? capturedFiscalYear = null;
        DateTimeOffset? capturedReservedAt = null;
        DateTimeOffset? capturedExpiresAt = null;
        _invoiceLeaseRepository.ReserveAsync(
            shop.Id,
            "device-1",
            Arg.Do<int>(value => capturedFiscalYear = value),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Do<DateTimeOffset>(value => capturedReservedAt = value),
            Arg.Do<DateTimeOffset>(value => capturedExpiresAt = value),
            Arg.Any<Guid?>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>()).Returns(lease);

        var handler = new ReserveInvoiceLeaseCommandHandler(_userRepository, _invoiceLeaseRepository, new FixedClock(now));
        var result = await handler.HandleAsync(new ReserveInvoiceLeaseCommand(actor.Id, shop.Id, "device-1", null), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(now, capturedReservedAt);
        Assert.Equal(DateTimeOffset.Parse(expectedExpiry, System.Globalization.CultureInfo.InvariantCulture), capturedExpiresAt);
        Assert.Equal(TimeSpan.Zero, capturedExpiresAt!.Value.Offset);
        Assert.Equal(fiscalYearStart, capturedFiscalYear);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
