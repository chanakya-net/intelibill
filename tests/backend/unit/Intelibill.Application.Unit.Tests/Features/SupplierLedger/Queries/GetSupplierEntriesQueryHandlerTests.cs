using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.SupplierLedger.Queries.GetSupplierEntries;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.SupplierLedger.Queries;

public class GetSupplierEntriesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly ISupplierLedgerEntryRepository _ledgerRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly GetSupplierEntriesQueryHandler _handler;

    public GetSupplierEntriesQueryHandlerTests()
    {
        _handler = new GetSupplierEntriesQueryHandler(_userRepository, _supplierRepository, _ledgerRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenCallerNotFound_ReturnsError()
    {
        _userRepository.GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _handler.HandleAsync(
            new GetSupplierEntriesQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenCallerHasNoMembership_ReturnsError()
    {
        var user = User.CreateWithEmail("user@test.com", "pass", "T", "U");
        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        var result = await _handler.HandleAsync(
            new GetSupplierEntriesQuery(user.Id, Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var user = User.CreateWithEmail("user@test.com", "pass", "T", "U");
        user.AddShopMembership(ShopMembership.Create(shopId, userId, ShopRole.Owner, false));

        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);
        _supplierRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Supplier?)null);

        var result = await _handler.HandleAsync(
            new GetSupplierEntriesQuery(userId, shopId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierBelongsToDifferentShop_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var user = User.CreateWithEmail("user@test.com", "pass", "T", "U");
        user.AddShopMembership(ShopMembership.Create(shopId, userId, ShopRole.Owner, false));
        var supplier = Supplier.Create(Guid.NewGuid(), "Other Shop Supplier", null, null, "Addr", "City", "State", "560001", true, false);

        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>())
            .Returns(supplier);

        var result = await _handler.HandleAsync(
            new GetSupplierEntriesQuery(userId, shopId, supplier.Id),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Supplier.SupplierNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_ReturnsLedgerEntries()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var user = User.CreateWithEmail("user@test.com", "pass", "T", "U");
        user.AddShopMembership(ShopMembership.Create(shopId, userId, ShopRole.Manager, false));
        var supplier = Supplier.Create(shopId, "Fresh Foods", null, null, "Addr", "City", "State", "560001", true, false);
        var entry = SupplierLedgerEntry.Create(shopId, supplier.Id, null, SupplierLedgerEntryType.PaymentMade, 500m, DateOnly.FromDateTime(DateTime.UtcNow), null, userId).Value;

        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);
        _supplierRepository.GetByIdAsync(supplier.Id, Arg.Any<CancellationToken>())
            .Returns(supplier);
        _ledgerRepository.GetBySupplierAsync(shopId, supplier.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { entry });

        var result = await _handler.HandleAsync(
            new GetSupplierEntriesQuery(userId, shopId, supplier.Id),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value);
        Assert.Equal(SupplierLedgerEntryType.PaymentMade, result.Value[0].EntryType);
    }
}
