using ErrorOr;
using Intelibill.Application.Features.SupplierLedger.Queries.GetSupplierEntries;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.SupplierLedger.Queries;

public class GetSupplierEntriesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly ISupplierLedgerEntryRepository _ledgerRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly GetSupplierEntriesQueryHandler _handler;

    public GetSupplierEntriesQueryHandlerTests()
    {
        _handler = new GetSupplierEntriesQueryHandler(_userRepository, _shopRepository, _supplierRepository, _ledgerRepository);
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
    }

    [Fact]
    public async Task HandleAsync_WhenShopNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var user = User.CreateWithEmail("user@test.com", "pass", "T", "U");
        user.AddShopMembership(ShopMembership.Create(shopId, userId, ShopRole.Owner, false));

        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdWithMembersAsync(shopId, Arg.Any<CancellationToken>())
            .Returns((Shop?)null);

        var result = await _handler.HandleAsync(
            new GetSupplierEntriesQuery(userId, shopId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task HandleAsync_WhenSupplierNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var user = User.CreateWithEmail("user@test.com", "pass", "T", "U");
        user.AddShopMembership(ShopMembership.Create(shopId, userId, ShopRole.Owner, false));
        var shop = Shop.Create("S", "A", "C", "S", "560001", null, null, null);
        shop.AddMembership(ShopMembership.Create(shopId, userId, ShopRole.Owner, false));

        _userRepository.GetByIdWithDetailsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdWithMembersAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(shop);
        _supplierRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Supplier?)null);

        var result = await _handler.HandleAsync(
            new GetSupplierEntriesQuery(userId, shopId, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsError);
    }
}
