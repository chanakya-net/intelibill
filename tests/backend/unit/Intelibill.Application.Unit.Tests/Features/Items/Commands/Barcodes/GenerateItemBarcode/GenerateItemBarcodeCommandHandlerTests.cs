using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Barcodes.GenerateItemBarcode;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Items.Commands.Barcodes.GenerateItemBarcode;

public class GenerateItemBarcodeCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemBarcodeSequenceRepository _itemBarcodeSequenceRepository = Substitute.For<IItemBarcodeSequenceRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();

    [Fact]
    public async Task HandleAsync_WhenActorMissing_ReturnsNotFound()
    {
        var handler = new GenerateItemBarcodeCommandHandler(
            _userRepository,
            _itemBarcodeSequenceRepository,
            _itemRepository);

        var result = await handler.HandleAsync(CreateCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Auth.UserNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenMembershipMissing_ReturnsForbidden()
    {
        var actor = CreateActor("member@test.com");
        var command = CreateCommand(actor.Id, Guid.NewGuid());
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new GenerateItemBarcodeCommandHandler(
            _userRepository,
            _itemBarcodeSequenceRepository,
            _itemRepository);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenActorIsStaff_ReturnsForbidden()
    {
        var actor = CreateActor("staff@test.com");
        var command = CreateCommand(actor.Id, Guid.NewGuid());
        actor.AddShopMembership(ShopMembership.Create(command.ActiveShopId, actor.Id, ShopRole.Staff, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new GenerateItemBarcodeCommandHandler(
            _userRepository,
            _itemBarcodeSequenceRepository,
            _itemRepository);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Item.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenSequenceMissing_CreatesSequenceAndReturnsBarcode()
    {
        var actor = CreateActor("owner@test.com");
        var command = CreateCommand(actor.Id, Guid.NewGuid());
        actor.AddShopMembership(ShopMembership.Create(command.ActiveShopId, actor.Id, ShopRole.Owner, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemBarcodeSequenceRepository.GetNextCodeAsync(command.ActiveShopId, Arg.Any<CancellationToken>()).Returns("IB-000001");
        _itemRepository.GetByBarcodeAsync(command.ActiveShopId, "IB-000001", Arg.Any<CancellationToken>()).Returns((Item?)null);

        var handler = new GenerateItemBarcodeCommandHandler(
            _userRepository,
            _itemBarcodeSequenceRepository,
            _itemRepository);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("IB-000001", result.Value.Barcode);
    }

    [Fact]
    public async Task HandleAsync_WhenCollisionSkipsAndReturnsNextAvailableCode()
    {
        var actor = CreateActor("manager@test.com");
        var command = CreateCommand(actor.Id, Guid.NewGuid());
        actor.AddShopMembership(ShopMembership.Create(command.ActiveShopId, actor.Id, ShopRole.Manager, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemBarcodeSequenceRepository.GetNextCodeAsync(command.ActiveShopId, Arg.Any<CancellationToken>())
            .Returns(_ => "IB-000001", _ => "IB-000002");
        _itemRepository.GetByBarcodeAsync(command.ActiveShopId, "IB-000001", Arg.Any<CancellationToken>())
            .Returns(CreateItem(command.ActiveShopId, "IB-000001"));
        _itemRepository.GetByBarcodeAsync(command.ActiveShopId, "IB-000002", Arg.Any<CancellationToken>())
            .Returns((Item?)null);

        var handler = new GenerateItemBarcodeCommandHandler(
            _userRepository,
            _itemBarcodeSequenceRepository,
            _itemRepository);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("IB-000002", result.Value.Barcode);
        await _itemRepository.Received(2).GetByBarcodeAsync(
            command.ActiveShopId,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSequenceRepositoryThrows_PropagatesError()
    {
        var actor = CreateActor("owner@test.com");
        var command = CreateCommand(actor.Id, Guid.NewGuid());
        actor.AddShopMembership(ShopMembership.Create(command.ActiveShopId, actor.Id, ShopRole.Owner, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _itemBarcodeSequenceRepository
            .GetNextCodeAsync(command.ActiveShopId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new InvalidOperationException("db")));

        var handler = new GenerateItemBarcodeCommandHandler(
            _userRepository,
            _itemBarcodeSequenceRepository,
            _itemRepository);

        var result = await Record.ExceptionAsync(() => handler.HandleAsync(command, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(result);
    }

    [Fact]
    public async Task HandleAsync_WhenSequenceGenerationIsCancelled_PropagatesCancellation()
    {
        var actor = CreateActor("owner@test.com");
        var command = CreateCommand(actor.Id, Guid.NewGuid());
        actor.AddShopMembership(ShopMembership.Create(command.ActiveShopId, actor.Id, ShopRole.Owner, true));
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _itemBarcodeSequenceRepository
            .GetNextCodeAsync(command.ActiveShopId, Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<string>(cts.Token));

        var handler = new GenerateItemBarcodeCommandHandler(
            _userRepository,
            _itemBarcodeSequenceRepository,
            _itemRepository);

        var result = await Record.ExceptionAsync(() => handler.HandleAsync(command, cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(result);
    }

    private static GenerateItemBarcodeCommand CreateCommand(Guid actorId, Guid activeShopId) =>
        new(actorId, activeShopId);

    private static User CreateActor(string email) =>
        User.CreateWithEmail(email, "hash", "First", "Last");

    private static Item CreateItem(Guid shopId, string barcode) =>
        Item.Create(
            shopId,
            "Existing",
            null,
            "pc",
            barcode,
            true,
            Guid.NewGuid());
}
