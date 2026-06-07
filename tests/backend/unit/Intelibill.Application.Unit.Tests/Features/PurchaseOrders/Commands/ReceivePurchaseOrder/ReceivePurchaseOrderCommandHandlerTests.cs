using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Application.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.ReceivePurchaseOrder;

public class ReceivePurchaseOrderCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPurchaseOrderRepository _poRepository = Substitute.For<IPurchaseOrderRepository>();
    private readonly IPurchaseOrderReceiptRepository _receiptRepository = Substitute.For<IPurchaseOrderReceiptRepository>();
    private readonly IInboundInventoryLineProcessor _inboundProcessor = Substitute.For<IInboundInventoryLineProcessor>();
    private readonly IPurchaseOrderReceiptNumberGenerator _receiptNumberGenerator = Substitute.For<IPurchaseOrderReceiptNumberGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private ReceivePurchaseOrderCommandHandler CreateHandler() =>
        new(_userRepository, _poRepository, _receiptRepository, _inboundProcessor, _receiptNumberGenerator, _unitOfWork);

    private static (User actor, Shop shop) MakeActor(ShopRole role = ShopRole.Owner)
    {
        var actor = User.CreateWithEmail($"{role.ToString().ToLowerInvariant()}@test.com", "hash", "Test", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, role, true));
        return (actor, shop);
    }

    private static PurchaseOrder MakePlacedPo(Guid shopId, Guid supplierId, int expectedQuantity = 3)
    {
        var po = PurchaseOrder.CreateDraft(shopId, "PO-2026-000001", supplierId, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", expectedQuantity, 10m);
        po.Place(supplierId);
        return po;
    }

    private static PurchaseOrder MakePlacedPoWithLines(Guid shopId, Guid supplierId, int expectedQuantity = 2)
    {
        var po = PurchaseOrder.CreateDraft(shopId, "PO-2026-000002", supplierId, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget A", expectedQuantity, 10m);
        po.AddLine(Guid.NewGuid(), "Widget B", expectedQuantity, 20m);
        po.Place(supplierId);
        return po;
    }

    private static ReceivePurchaseOrderCommand MakeCommand(Guid actorId, Guid shopId, PurchaseOrder po, Guid lineId, int quantity = 1) =>
        new(
            actorId,
            shopId,
            po.Id,
            "REF-1",
            "Received",
            new DateTimeOffset(2026, 6, 7, 10, 0, 0, TimeSpan.Zero),
            [
                new ReceivePurchaseOrderLineInput(
                    lineId,
                    $"B-{Guid.NewGuid():N}",
                    quantity,
                    10m,
                    12m,
                    11m,
                    5m,
                    false,
                    false,
                    null,
                    null),
            ]);

    private static InboundInventoryLineResult MakeInboundResult(Guid shopId, Guid itemId, Guid supplierId, Guid actorId)
    {
        var item = Item.Create(shopId, "Widget", null, "pcs", $"ITM-{Guid.NewGuid():N}", true, actorId);
        var batch = InventoryBatch.Create(shopId, itemId, $"B-{Guid.NewGuid():N}", 1, 10m, 12m, 11m, 5m, false, null, null, supplierId, actorId).Value;
        var stock = StockTransaction.Create(shopId, itemId, batch.Id, StockTransactionType.In, 1, "REF-1", "Received", DateTimeOffset.UtcNow, actorId, actorId).Value;
        var ledger = SupplierLedgerEntry.Create(shopId, supplierId, batch.Id, SupplierLedgerEntryType.GoodsReceived, 10m, DateOnly.FromDateTime(DateTime.UtcNow), null, actorId).Value;
        var inventory = Domain.Entities.Inventory.Create(shopId, itemId, 1, 0, 0, actorId).Value;
        return new InboundInventoryLineResult(item, batch, stock, ledger, inventory);
    }

    [Fact]
    public async Task HandleAsync_WhenStaff_ReturnsForbidden()
    {
        var (actor, shop) = MakeActor(ShopRole.Staff);
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(
            new ReceivePurchaseOrderCommand(actor.Id, shop.Id, Guid.NewGuid(), null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.UserCannotMutatePurchaseOrder.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenInvalidStatus_RollsBackAndReturnsValidationError()
    {
        var (actor, shop) = MakeActor();
        var po = PurchaseOrder.CreateDraft(shop.Id, Guid.NewGuid().ToString("N"), Guid.NewGuid(), null, null, null, null);
        var line = po.AddLine(Guid.NewGuid(), "Widget", 1, 10m);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetReceiptDetailForUpdateAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(MakeCommand(actor.Id, shop.Id, po, line.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.CannotReceiveInvalidStatus.Code, result.FirstError.Code);
        await _unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenQuantityExceedsLockedRemaining_RollsBackWithoutInboundSideEffects()
    {
        var (actor, shop) = MakeActor();
        var supplierId = Guid.NewGuid();
        var po = MakePlacedPo(shop.Id, supplierId, expectedQuantity: 1);
        var line = po.Lines.Single();

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetReceiptDetailForUpdateAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);

        var result = await CreateHandler().HandleAsync(MakeCommand(actor.Id, shop.Id, po, line.Id, quantity: 2), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.ReceiptQuantityOverRemaining.Code, result.FirstError.Code);
        await _inboundProcessor.DidNotReceive().ProcessAsync(
            Arg.Any<Guid>(),
            Arg.Any<InboundInventoryLineInput>(),
            Arg.Any<Guid>(),
            Arg.Any<ItemResolutionContext>(),
            Arg.Any<InventoryUpdateContext>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenInboundFails_RollsBackReceipt()
    {
        var (actor, shop) = MakeActor();
        var supplierId = Guid.NewGuid();
        var po = MakePlacedPo(shop.Id, supplierId);
        var line = po.Lines.Single();

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetReceiptDetailForUpdateAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _receiptNumberGenerator.GenerateAsync(shop.Id, 2026, Arg.Any<CancellationToken>()).Returns("POR-2026-000001");
        _inboundProcessor.ProcessAsync(
                Arg.Any<Guid>(),
                Arg.Any<InboundInventoryLineInput>(),
                Arg.Any<Guid>(),
                Arg.Any<ItemResolutionContext>(),
                Arg.Any<InventoryUpdateContext>(),
                Arg.Any<CancellationToken>())
            .Returns(Errors.Inventory.BatchNumberAlreadyExists);

        var result = await CreateHandler().HandleAsync(MakeCommand(actor.Id, shop.Id, po, line.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Inventory.BatchNumberAlreadyExists.Code, result.FirstError.Code);
        await _unitOfWork.Received(1).RollbackTransactionAsync(Arg.Any<CancellationToken>());
        await _receiptRepository.DidNotReceive().AddAsync(Arg.Any<PurchaseOrderReceipt>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReceivingPartialQuantity_PersistsReceiptAndPartiallyReceivedStatus()
    {
        var (actor, shop) = MakeActor(ShopRole.Manager);
        var supplierId = Guid.NewGuid();
        var po = MakePlacedPo(shop.Id, supplierId, expectedQuantity: 3);
        var line = po.Lines.Single();

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetReceiptDetailForUpdateAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _receiptNumberGenerator.GenerateAsync(shop.Id, 2026, Arg.Any<CancellationToken>()).Returns("POR-2026-000001");
        _inboundProcessor.ProcessAsync(
                shop.Id,
                Arg.Any<InboundInventoryLineInput>(),
                actor.Id,
                Arg.Any<ItemResolutionContext>(),
                Arg.Any<InventoryUpdateContext>(),
                Arg.Any<CancellationToken>())
            .Returns(MakeInboundResult(shop.Id, line.ItemId, supplierId, actor.Id));

        var result = await CreateHandler().HandleAsync(MakeCommand(actor.Id, shop.Id, po, line.Id, quantity: 1), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, result.Value.Status);
        Assert.Equal(1, result.Value.Lines.Single().ReceivedQuantity);
        await _receiptRepository.Received(1).AddAsync(
            Arg.Is<PurchaseOrderReceipt>(receipt => receipt.Lines.Count == 1),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReceivingMultipleLines_PersistsOneReceiptAndReceivedStatus()
    {
        var (actor, shop) = MakeActor(ShopRole.Manager);
        var supplierId = Guid.NewGuid();
        var po = MakePlacedPoWithLines(shop.Id, supplierId, expectedQuantity: 1);
        var lines = po.Lines.ToArray();

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetReceiptDetailForUpdateAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _receiptNumberGenerator.GenerateAsync(shop.Id, 2026, Arg.Any<CancellationToken>()).Returns("POR-2026-000001");
        _inboundProcessor.ProcessAsync(
                shop.Id,
                Arg.Any<InboundInventoryLineInput>(),
                actor.Id,
                Arg.Any<ItemResolutionContext>(),
                Arg.Any<InventoryUpdateContext>(),
                Arg.Any<CancellationToken>())
            .Returns(
                MakeInboundResult(shop.Id, lines[0].ItemId, supplierId, actor.Id),
                MakeInboundResult(shop.Id, lines[1].ItemId, supplierId, actor.Id));

        var command = new ReceivePurchaseOrderCommand(
            actor.Id,
            shop.Id,
            po.Id,
            "REF-1",
            "Received",
            new DateTimeOffset(2026, 6, 7, 10, 0, 0, TimeSpan.Zero),
            lines.Select(line => new ReceivePurchaseOrderLineInput(
                line.Id,
                $"B-{Guid.NewGuid():N}",
                1,
                line.UnitCost,
                line.UnitCost + 2,
                line.UnitCost + 1,
                5m,
                false,
                false,
                null,
                null)).ToList());

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(PurchaseOrderStatus.Received, result.Value.Status);
        Assert.All(result.Value.Lines, line => Assert.Equal(1, line.ReceivedQuantity));
        await _receiptRepository.Received(1).AddAsync(
            Arg.Is<PurchaseOrderReceipt>(receipt => receipt.Lines.Count == 2),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicateReceiptLine_ReturnsValidationWithoutTransaction()
    {
        var (actor, shop) = MakeActor();
        var po = MakePlacedPo(shop.Id, Guid.NewGuid());
        var line = po.Lines.Single();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var command = MakeCommand(actor.Id, shop.Id, po, line.Id) with
        {
            Lines =
            [
                new ReceivePurchaseOrderLineInput(line.Id, "B-1", 1, 10m, 12m, 11m, 5m, false, false, null, null),
                new ReceivePurchaseOrderLineInput(line.Id, "B-2", 1, 10m, 12m, 11m, 5m, false, false, null, null),
            ],
        };

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.DuplicateReceiptLine.Code, result.FirstError.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReceivingRemainingQuantity_PersistsReceivedStatus()
    {
        var (actor, shop) = MakeActor();
        var supplierId = Guid.NewGuid();
        var po = MakePlacedPo(shop.Id, supplierId, expectedQuantity: 1);
        var line = po.Lines.Single();

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _poRepository.GetReceiptDetailForUpdateAsync(shop.Id, po.Id, Arg.Any<CancellationToken>()).Returns(po);
        _receiptNumberGenerator.GenerateAsync(shop.Id, 2026, Arg.Any<CancellationToken>()).Returns("POR-2026-000001");
        _inboundProcessor.ProcessAsync(
                shop.Id,
                Arg.Any<InboundInventoryLineInput>(),
                actor.Id,
                Arg.Any<ItemResolutionContext>(),
                Arg.Any<InventoryUpdateContext>(),
                Arg.Any<CancellationToken>())
            .Returns(MakeInboundResult(shop.Id, line.ItemId, supplierId, actor.Id));

        var result = await CreateHandler().HandleAsync(MakeCommand(actor.Id, shop.Id, po, line.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(PurchaseOrderStatus.Received, result.Value.Status);
        Assert.Equal(1, result.Value.Lines.Single().ReceivedQuantity);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
