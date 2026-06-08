using Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Shops.Commands.UpdateShopBankDetails;

public class UpdateShopBankDetailsCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IBankAccountRepository _bankAccountRepository = Substitute.For<IBankAccountRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenPrimaryAccountExists_UpdatesAccount()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true);
        var account = BankAccount.Create(shop.Id, "Old Bank", "111", BankAccountType.Savings, "SBIN0001111", "Old Holder");
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(shop);
        _bankAccountRepository.FindAsync(Arg.Any<System.Linq.Expressions.Expression<Func<BankAccount, bool>>>(), Arg.Any<CancellationToken>())
            .Returns([account]);

        var handler = new UpdateShopBankDetailsCommandHandler(
            _userRepository,
            _shopRepository,
            _bankAccountRepository,
            _unitOfWork);

        var result = await handler.HandleAsync(new UpdateShopBankDetailsCommand(
            user.Id,
            shop.Id,
            "New Bank",
            "222",
            "Current",
            "SBIN0002222",
            "New Holder"), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(shop.Id, result.Value.ShopId);
        Assert.Equal("New Bank", result.Value.BankName);
        Assert.Equal("222", result.Value.BankAccountNumber);
        Assert.Equal("Current", result.Value.BankAccountType);
        Assert.Equal("SBIN0002222", result.Value.IfscCode);
        Assert.Equal("New Holder", result.Value.AccountHolderName);

        _bankAccountRepository.Received(1).Update(account);
        await _bankAccountRepository.DidNotReceive().AddAsync(Arg.Any<BankAccount>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
