using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Barcodes;
using Intelibill.Application.Features.Items.Barcodes.PrintBarcodeLabels;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Items.Commands.Barcodes.PrintBarcodeLabels;

public sealed class PrintBarcodeLabelsCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IBarcodeLabelRepository _barcodeLabelRepository = Substitute.For<IBarcodeLabelRepository>();
    private readonly IBarcodeLabelPdfRenderer _barcodeLabelPdfRenderer = Substitute.For<IBarcodeLabelPdfRenderer>();

    [Fact]
    public async Task HandleAsync_WhenActorMissing_ReturnsNotFound()
    {
        var handler = new PrintBarcodeLabelsCommandHandler(
            _userRepository,
            _barcodeLabelRepository,
            _barcodeLabelPdfRenderer);

        var result = await handler.HandleAsync(CreateCommand(Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), 1, null)]), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenActorIsStaff_ReturnsForbidden()
    {
        var command = CreateCommand(Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), 1, null)]);
        var actor = CreateActor("staff@labels.test");
        actor.AddShopMembership(ShopMembership.Create(command.ActiveShopId, actor.Id, ShopRole.Staff, true));
        _userRepository.GetByIdWithDetailsAsync(command.ActorUserId, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new PrintBarcodeLabelsCommandHandler(
            _userRepository,
            _barcodeLabelRepository,
            _barcodeLabelPdfRenderer);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Item.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenItemMissing_ReturnsValidationError()
    {
        var command = CreateCommand(Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), 1, null)]);
        var actor = CreateActor("owner@labels.test");
        actor.AddShopMembership(ShopMembership.Create(command.ActiveShopId, actor.Id, ShopRole.Owner, true));
        _userRepository.GetByIdWithDetailsAsync(command.ActorUserId, Arg.Any<CancellationToken>()).Returns(actor);
        _barcodeLabelRepository
            .GetRowsAsync(command.ActiveShopId, Arg.Any<IReadOnlyList<PrintBarcodeLabelItemRequest>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var handler = new PrintBarcodeLabelsCommandHandler(
            _userRepository,
            _barcodeLabelRepository,
            _barcodeLabelPdfRenderer);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Item.BarcodeLabelItemNotFound(command.Items[0].ItemId).Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenBatchMissing_ReturnsValidationError()
    {
        var requestedBatchId = Guid.NewGuid();
        var command = CreateCommand(Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), 1, requestedBatchId)]);
        var actor = CreateActor("manager@labels.test");
        actor.AddShopMembership(ShopMembership.Create(command.ActiveShopId, actor.Id, ShopRole.Manager, true));
        _userRepository.GetByIdWithDetailsAsync(command.ActorUserId, Arg.Any<CancellationToken>()).Returns(actor);
        _barcodeLabelRepository
            .GetRowsAsync(command.ActiveShopId, Arg.Any<IReadOnlyList<PrintBarcodeLabelItemRequest>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var handler = new PrintBarcodeLabelsCommandHandler(
            _userRepository,
            _barcodeLabelRepository,
            _barcodeLabelPdfRenderer);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Item.BarcodeLabelBatchNotFound(command.Items[0].ItemId, requestedBatchId).Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenRequestValid_ExpandsRowsByQuantityAndReturnsPdf()
    {
        var itemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var command = CreateCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [
                new(itemId, 2, null),
                new(secondItemId, 1, batchId),
            ]);

        var actor = CreateActor("owner@labels.test");
        actor.AddShopMembership(ShopMembership.Create(command.ActiveShopId, actor.Id, ShopRole.Owner, true));
        _userRepository.GetByIdWithDetailsAsync(command.ActorUserId, Arg.Any<CancellationToken>()).Returns(actor);
        _barcodeLabelRepository
            .GetRowsAsync(command.ActiveShopId, Arg.Any<IReadOnlyList<PrintBarcodeLabelItemRequest>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new BarcodeLabelPrintRow(itemId, null, "Toor Dal", "IB-000001", "Green Mart", 120m, 110m),
                new BarcodeLabelPrintRow(secondItemId, batchId, "Rice", "IB-000002", "Green Mart", 80m, 75m),
            ]);

        var expectedResult = new BarcodeLabelPrintResult([1, 2, 3], "application/pdf", "barcode-labels.pdf");
        _barcodeLabelPdfRenderer
            .RenderAsync(Arg.Any<BarcodeLabelPrintDataset>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        var handler = new PrintBarcodeLabelsCommandHandler(
            _userRepository,
            _barcodeLabelRepository,
            _barcodeLabelPdfRenderer);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(expectedResult, result.Value);
        await _barcodeLabelPdfRenderer.Received(1).RenderAsync(
            Arg.Is<BarcodeLabelPrintDataset>(dataset =>
                dataset.Rows.Count == 3
                && dataset.Rows.Count(row => row.ItemId == itemId && row.InventoryBatchId == null) == 2
                && dataset.Rows.Count(row => row.ItemId == secondItemId && row.InventoryBatchId == batchId) == 1),
            Arg.Any<CancellationToken>());
    }

    private static PrintBarcodeLabelsCommand CreateCommand(
        Guid actorUserId,
        Guid activeShopId,
        IReadOnlyList<PrintBarcodeLabelItemRequest> items) =>
        new(actorUserId, activeShopId, items);

    private static User CreateActor(string email) =>
        User.CreateWithEmail(email, "hash", "First", "Last");
}
