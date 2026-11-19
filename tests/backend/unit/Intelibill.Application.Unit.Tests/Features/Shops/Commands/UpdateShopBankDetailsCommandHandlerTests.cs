using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.UpdateShopBankDetails;

public class UpdateShopBankDetailsCommandHandlerTests
{
    private readonly ShopSetupAccessFixture _setup = new();
    private readonly IBankAccountRepository _bankAccountRepository = Substitute.For<IBankAccountRepository>();

    [Fact]
    public async Task HandleAsync_WhenPrimaryAccountExists_UpdatesAccount()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var account = BankAccount.Create(shop.Id, "Old Bank", "111", BankAccountType.Savings, "SBIN0001111", "Old Holder");
        var session = _setup.Member(shop.Id, user, ShopRole.Owner, shop);
        _bankAccountRepository.FindAsync(Arg.Any<System.Linq.Expressions.Expression<Func<BankAccount, bool>>>(), Arg.Any<CancellationToken>())
            .Returns([account]);

        var handler = new UpdateShopBankDetailsCommandHandler(_setup.Access, _bankAccountRepository);

        var result = await handler.HandleAsync(new UpdateShopBankDetailsCommand(
            user.Id,
            shop.Id,
            "New Bank",
            "222",
            "Current",
            "SBIN0002222",
            "New Holder") { Session = session }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(shop.Id, result.Value.ShopId);
        Assert.Equal("New Bank", result.Value.BankName);
        Assert.Equal("222", result.Value.BankAccountNumber);
        Assert.Equal("Current", result.Value.BankAccountType);
        Assert.Equal("SBIN0002222", result.Value.IfscCode);
        Assert.Equal("New Holder", result.Value.AccountHolderName);

        _bankAccountRepository.Received(1).Update(account);
        await _bankAccountRepository.DidNotReceive().AddAsync(Arg.Any<BankAccount>(), Arg.Any<CancellationToken>());
        await _setup.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
