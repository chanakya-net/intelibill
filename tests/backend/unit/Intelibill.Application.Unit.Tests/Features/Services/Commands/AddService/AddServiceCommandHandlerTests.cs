using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Services.Commands.AddService;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Services.Commands.AddService;

public class AddServiceCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IServiceRepository _serviceRepository = Substitute.For<IServiceRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenDuplicateName_ReturnsConflict()
    {
        var actor = User.CreateWithEmail("owner@test.com", "hash", "Owner", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Owner, true));

        var existing = Service.Create(
            shop.Id,
            "SRV-000001",
            "Consulting",
            null,
            100m,
            null,
            18m,
            false,
            true,
            actor.Id);

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _serviceRepository.GetByNameAsync(shop.Id, "Consulting", Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new AddServiceCommandHandler(_userRepository, _serviceRepository, _unitOfWork);

        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Service.NameAlreadyExists.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenActorIsStaff_ReturnsForbidden()
    {
        var actor = User.CreateWithEmail("staff@test.com", "hash", "Staff", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Staff, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);

        var handler = new AddServiceCommandHandler(_userRepository, _serviceRepository, _unitOfWork);

        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Service.UserIsNotOwnerOrManager.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_CreatesServiceWithGeneratedCode()
    {
        var actor = User.CreateWithEmail("manager@test.com", "hash", "Manager", "User");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        actor.AddShopMembership(ShopMembership.Create(shop.Id, actor.Id, ShopRole.Manager, true));

        _userRepository.GetByIdWithDetailsAsync(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        _serviceRepository.GetByNameAsync(shop.Id, "Consulting", Arg.Any<CancellationToken>()).Returns((Service?)null);
        _serviceRepository.GetNextCodeAsync(shop.Id, Arg.Any<CancellationToken>()).Returns("SRV-000001");
        _serviceRepository.GetByCodeAsync(shop.Id, "SRV-000001", Arg.Any<CancellationToken>()).Returns((Service?)null);

        var handler = new AddServiceCommandHandler(_userRepository, _serviceRepository, _unitOfWork);
        var result = await handler.HandleAsync(CreateCommand(actor.Id, shop.Id), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("SRV-000001", result.Value.Code);
        Assert.Equal("Consulting", result.Value.Name);
        Assert.Equal(120m, result.Value.Price);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _serviceRepository.Received(1).AddAsync(
            Arg.Is<Service>(s =>
                s.Code == "SRV-000001" &&
                s.Name == "Consulting" &&
                s.ShopId == shop.Id &&
                s.Price == 120m),
            Arg.Any<CancellationToken>());
    }

    private static AddServiceCommand CreateCommand(Guid actorId, Guid shopId) =>
        new(
            actorId,
            shopId,
            Name: "Consulting",
            Description: null,
            Price: 120m,
            HsnCode: "1001",
            TaxRatePercent: 18m,
            TaxIncluded: false,
            IsActive: true);
}
