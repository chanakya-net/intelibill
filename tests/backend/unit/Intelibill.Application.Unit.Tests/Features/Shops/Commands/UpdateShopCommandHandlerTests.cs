using Intelibill.Application.Features.Shops.Commands.UpdateShop;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.UpdateShop;

public class UpdateShopCommandHandlerTests
{
    private readonly ShopSetupAccessFixture _fixture = new();

    [Fact]
    public async Task Owner_updates_normalized_details_and_saves_once()
    {
        var owner = NewUser();
        var shop = Shop.Create("Old Shop", "Old Road", "City", "State", "560002", null, null, null);
        var cmd = Command(owner.Id, shop.Id) with { Session = _fixture.Member(shop.Id, owner, ShopRole.Owner, shop) };
        var result = await Handler().HandleAsync(cmd, default);

        Assert.False(result.IsError);
        var details = result.Value;
        Assert.Equal(("Main Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001"),
            (details.Name, details.Address, details.City, details.State, details.Pincode));
        Assert.Equal(("Chandra", "9876543210", "27AAPFU0939F1ZV"), (details.ContactPerson, details.MobileNumber, details.GstNumber));
        await _fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _fixture.UnitOfWork.Received(1).CommitTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, false, "Auth.SessionInvalidated")]
    [InlineData(null, true, "Shop.MembershipNotFound")]
    [InlineData(ShopRole.Manager, true, "Shop.UserIsNotOwner")]
    [InlineData(ShopRole.Owner, true, "Shop.ShopNotFound")]
    public async Task Invalid_authority_or_target_cannot_save(ShopRole? role, bool validSession, string code)
    {
        var actor = NewUser();
        var shop = Guid.NewGuid();
        var session = !validSession ? null : role is { } memberRole
            ? _fixture.Member(shop, actor, memberRole) : _fixture.Session(actor.Id, shop);
        var result = await Handler().HandleAsync(Command(actor.Id, shop) with { Session = session }, default);

        Assert.Equal(code, result.FirstError.Code);
        await _fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private UpdateShopCommandHandler Handler() => new(_fixture.Access, Substitute.For<IBankAccountRepository>());

    private static User NewUser() => User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");

    private static UpdateShopCommand Command(Guid actor, Guid shop) =>
        new(actor, shop, " Main Shop ", " 42 MG Road ", " Bengaluru ", " Karnataka ", " 560001 ",
            " Chandra ", " 9876543210 ", " 27AAPFU0939F1ZV ");
}
