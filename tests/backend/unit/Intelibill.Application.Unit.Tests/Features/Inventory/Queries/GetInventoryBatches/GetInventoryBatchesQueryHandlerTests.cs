using Intelibill.Application.Unit.Tests.Common.Time;
using ErrorOr;
using Intelibill.Application.Features.Inventory.Queries.GetInventoryBatches;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Queries.GetInventoryBatches;

public class GetInventoryBatchesQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IPurchaseReceiptValueRepository _receiptRepository = Substitute.For<IPurchaseReceiptValueRepository>();
    private readonly GetInventoryBatchesQueryHandler _handler;

    public GetInventoryBatchesQueryHandlerTests()
    {
        _handler = new GetInventoryBatchesQueryHandler(_userRepository, _shopRepository, _batchRepository, _receiptRepository, ExpiryTestDoubles.Guard(), BusinessTestDoubles.RealClock());
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var query = new GetInventoryBatchesQuery(Guid.NewGuid(), Guid.NewGuid());

        var result = await _handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsError()
    {
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("user@test.com", "hashedpass", "Test", "User"));
        _shopRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Shop?)null);

        var query = new GetInventoryBatchesQuery(Guid.NewGuid(), Guid.NewGuid());

        var result = await _handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("user@test.com", "hashedpass", "Test", "User"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("Test Shop", "Addr", "City", "State", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var query = new GetInventoryBatchesQuery(userId, shopId);

        var result = await _handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenValid_ReturnsMappedBatchDtos()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var user = User.CreateWithEmail("user@test.com", "hashedpass", "Test", "User");
        var shop = Shop.Create("Test Shop", "Addr", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>()).Returns(membership);
        _batchRepository.GetByShopAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<InventoryBatch>());

        var query = new GetInventoryBatchesQuery(userId, shopId);

        var result = await _handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_WhenValid_CarriesTheItemUnitAndPrecision()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var item = Item.Create(shopId, "Rice", null, "kg", "111", true, userId);
        var batch = InventoryBatch.Create(shopId, item.Id, "B-01", 5m, 80m, 120m, 100m, 18m, false, null, null, null, userId).Value;
        typeof(InventoryBatch).GetProperty(nameof(InventoryBatch.Item))!.SetValue(batch, item);

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("user@test.com", "hashedpass", "Test", "User"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("Test Shop", "Addr", "City", "State", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, true));
        _batchRepository.GetByShopAsync(shopId, Arg.Any<CancellationToken>()).Returns([batch]);
        _receiptRepository.GetByShopAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PurchaseReceiptValue>());

        var result = await _handler.Handle(new GetInventoryBatchesQuery(userId, shopId), CancellationToken.None);

        Assert.False(result.IsError);
        var dto = Assert.Single(result.Value);
        Assert.Equal("kg", dto.Uom);
        Assert.Equal(3, dto.QuantityScale);
    }
}
