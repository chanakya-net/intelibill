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
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private UpdateShopBankDetailsCommandHandler CreateHandler(bool withValidator = false) =>
        new(_userRepository, _shopRepository, _unitOfWork,
            withValidator ? new UpdateShopBankDetailsCommandValidator() : null);

    [Fact]
    public async Task HandleAsync_OwnerWithValidData_UpdatesBankDetails()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        var command = new UpdateShopBankDetailsCommand(
            user.Id,
            shop.Id,
            "State Bank of India",
            "123456789012",
            "Savings",
            "SBIN0001234",
            "Chandra Kumar");

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("State Bank of India", result.Value.BankName);
        Assert.Equal("123456789012", result.Value.BankAccountNumber);
        Assert.Equal("Savings", result.Value.BankAccountType);
        Assert.Equal("SBIN0001234", result.Value.IfscCode);
        Assert.Equal("Chandra Kumar", result.Value.AccountHolderName);

        _shopRepository.Received(1).Update(shop);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NonOwner_ReturnsForbidden()
    {
        var user = User.CreateWithEmail("staff@test.com", "hash", "Staff", "One");
        var shop = Shop.Create("Main Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Staff, false);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        var command = new UpdateShopBankDetailsCommand(user.Id, shop.Id, "SBI", null, null, null, null);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Shop.UserIsNotOwner", result.FirstError.Code);
        _shopRepository.DidNotReceive().Update(Arg.Any<Shop>());
    }

    [Fact]
    public async Task HandleAsync_WhenIfscInvalid_ReturnsValidationError()
    {
        var command = new UpdateShopBankDetailsCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            "INVALID_IFSC",
            null);

        var result = await CreateHandler(withValidator: true).HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Shop.IfscCodeInvalid", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAccountTypeInvalid_ReturnsValidationError()
    {
        var command = new UpdateShopBankDetailsCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            "InvalidType",
            null,
            null);

        var result = await CreateHandler(withValidator: true).HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Shop.BankAccountTypeInvalid", result.FirstError.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AllNullBankDetails_ClearsBankFields()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var shop = Shop.Create("Main Shop", "42 MG Road", "Bengaluru", "Karnataka", "560001", null, null, null);
        shop.UpdateBankDetails("SBI", "12345", BankAccountType.Savings, "SBIN0001234", "Chandra");
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true);
        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);

        var command = new UpdateShopBankDetailsCommand(user.Id, shop.Id, null, null, null, null, null);

        var result = await CreateHandler().HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value.BankName);
        Assert.Null(result.Value.BankAccountNumber);
        Assert.Null(result.Value.BankAccountType);
        Assert.Null(result.Value.IfscCode);
        Assert.Null(result.Value.AccountHolderName);
    }
}
