using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;
using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.PurchaseOrders.Commands.CreatePurchaseOrderDraft;

public class CreatePurchaseOrderDraftCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPurchaseOrderRepository _poRepository = Substitute.For<IPurchaseOrderRepository>();
    private readonly IPurchaseOrderNumberGenerator _numberGenerator = Substitute.For<IPurchaseOrderNumberGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private CreatePurchaseOrderDraftCommandHandler CreateHandler() =>
        new(_userRepository, _poRepository, _numberGenerator, _unitOfWork);

    private static (User actor, Shop shop) MakeOwner()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));
        return (actor, shop);
    }

    [Fact]
    public async Task HandleAsync_WhenStaff_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(actor.Id, shop.Id, null, null, null, []),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.UserCannotCreatePurchaseOrder.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenManager_Succeeds()
    {
        var actor = User.CreateWithEmail("mgr@test.com", "hash", "Mgr", "User");
        var shop = Shop.Create("Main", "Addr", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                "Test notes",
                "Acme Traders",
                "SUP-REF-001",
                [new CreatePurchaseOrderLineInput("Widget", 5, 10m)]),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("PO-2026-000001", result.Value.PurchaseOrderNumber);
        Assert.Equal("Test notes", result.Value.Notes);
        Assert.Single(result.Value.Lines);
        Assert.Equal(50m, result.Value.ExpectedTotal);
    }

    [Fact]
    public async Task HandleAsync_ZeroQuantity_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                [new CreatePurchaseOrderLineInput("Widget", 0, 10m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.InvalidLineQuantity.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_NegativeUnitCost_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                [new CreatePurchaseOrderLineInput("Widget", 1, -1m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.InvalidLineUnitCost.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_EmptyLineDescription_ReturnsValidationError()
    {
        var (actor, shop) = MakeOwner();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                null,
                null,
                [new CreatePurchaseOrderLineInput("   ", 1, 5m)]),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.PurchaseOrder.LineDescriptionRequired.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidOwner_AddsPoAndSaves()
    {
        var (actor, shop) = MakeOwner();
        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _numberGenerator.GenerateAsync(shop.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("PO-2026-000001");

        var result = await CreateHandler().HandleAsync(
            new CreatePurchaseOrderDraftCommand(
                actor.Id,
                shop.Id,
                null,
                " Acme Traders ",
                " SUP-REF-001 ",
                [new CreatePurchaseOrderLineInput("Item A", 2, 100m)]),
            CancellationToken.None);

        Assert.False(result.IsError);
        await _poRepository.Received(1).AddAsync(
            Arg.Is<PurchaseOrder>(po =>
                po.ShopId == shop.Id
                && po.PurchaseOrderNumber == "PO-2026-000001"
                && po.SupplierName == "Acme Traders"
                && po.SupplierReference == "SUP-REF-001"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
