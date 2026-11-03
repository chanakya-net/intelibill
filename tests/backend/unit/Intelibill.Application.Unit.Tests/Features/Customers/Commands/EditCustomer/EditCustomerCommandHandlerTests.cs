using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.Commands.EditCustomer;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Commands.EditCustomer;

public class EditCustomerCommandHandlerTests
{
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly EditCustomerCommandHandler _handler;

    public EditCustomerCommandHandlerTests()
    {
        _handler = new EditCustomerCommandHandler(_customerRepository, _unitOfWork);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerNotFoundInShop_ReturnsNotFoundError()
    {
        var shopId = Guid.NewGuid();
        _customerRepository.GetByShopAndIdAsync(shopId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var command = new EditCustomerCommand(shopId, Guid.NewGuid(), "New Name", "+919876543210", null, true, 0m);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Customer.CustomerNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_UpdatesCustomerAndReturnsDto()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Old Name", "+911234567890", null);
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);

        var command = new EditCustomerCommand(shopId, customer.Id, "New Name", "+919999999999", "12 MG Road", false, 750m);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("New Name", result.Value.Name);
        Assert.Equal("+919999999999", result.Value.PhoneNumber);
        Assert.Equal("12 MG Road", result.Value.Address);
        Assert.False(result.Value.IsActive);
        Assert.Equal(750m, result.Value.CreditLimit);
        _customerRepository.Received(1).Update(customer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenGstinOmitted_KeepsTheRecordedRegistration()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Buyer", "+911234567890", "4 Market St", gstin: "33AABCT1332L1ZL");
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, customer.Id, "Buyer Renamed", "+911234567890", "4 Market St", true, 0m),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("33AABCT1332L1ZL", customer.Gstin);
        Assert.Equal("33", customer.StateCode);
        Assert.Equal("33AABCT1332L1ZL", result.Value.Gstin);
    }

    [Fact]
    public async Task HandleAsync_WhenGstinEmpty_ClearsIt()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Buyer", "+911234567890", "4 Market St", gstin: "33AABCT1332L1ZL");
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, customer.Id, "Buyer", "+911234567890", "4 Market St", true, 0m, Gstin: "", StateCode: "29"),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(customer.Gstin);
        Assert.Equal("29", customer.StateCode);
    }

    [Fact]
    public async Task HandleAsync_WhenStateContradictsTheKeptGstin_IsRefused()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Buyer", "+911234567890", "4 Market St", gstin: "33AABCT1332L1ZL");
        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await _handler.HandleAsync(
            new EditCustomerCommand(shopId, customer.Id, "Buyer", "+911234567890", "4 Market St", true, 0m, StateCode: "29"),
            CancellationToken.None);

        Assert.Equal(Errors.Customer.GstinStateMismatch.Code, result.FirstError.Code);
        Assert.Equal("33", customer.StateCode);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
