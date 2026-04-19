using ErrorOr;
using Intelibill.Application.Features.Inventory.Commands.UpdateInventoryBatch;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Commands.UpdateInventoryBatch;

public class UpdateInventoryBatchCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly ISupplierRepository _supplierRepository = Substitute.For<ISupplierRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IInventoryRepository _inventoryRepository = Substitute.For<IInventoryRepository>();
    private readonly IStockTransactionRepository _stockTransactionRepository = Substitute.For<IStockTransactionRepository>();
    private readonly ISupplierLedgerEntryRepository _ledgerRepository = Substitute.For<ISupplierLedgerEntryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly UpdateInventoryBatchCommandHandler _handler;

    public UpdateInventoryBatchCommandHandlerTests()
    {
        _handler = new UpdateInventoryBatchCommandHandler(
            _userRepository, _shopRepository, _supplierRepository,
            _batchRepository, _inventoryRepository, _stockTransactionRepository,
            _ledgerRepository, _unitOfWork);
    }

    private static UpdateInventoryBatchCommand ValidCommand(Guid userId, Guid shopId, Guid batchId) =>
        new(batchId, userId, shopId, "BATCH-CORR", 10m, 100m, 200m, 180m, 18m, false, null, null, null, null, null);

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var result = await _handler.Handle(ValidCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Shop?)null);

        var result = await _handler.Handle(ValidCommand(userId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("S", "A", "C", "S", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var result = await _handler.Handle(ValidCommand(userId, shopId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenMembershipRoleIsStaff_ReturnsForbiddenError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("S", "A", "C", "S", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Staff, false));

        var result = await _handler.Handle(ValidCommand(userId, shopId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_WhenBatchNotFound_ReturnsError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("S", "A", "C", "S", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, false));
        _batchRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var result = await _handler.Handle(ValidCommand(userId, shopId, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Handle_WhenBatchAlreadyVoided_ReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var batchResult = InventoryBatch.Create(shopId, itemId, "BATCH-001", 10m, 100m, 200m, 180m, 18m, false, null, null, null, userId);
        var batch = batchResult.Value;
        batch.Void(userId);

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("u@t.com", "pass", "T", "U"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("S", "A", "C", "S", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, false));
        _batchRepository.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(batch);

        var result = await _handler.Handle(ValidCommand(userId, shopId, batch.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }
}
