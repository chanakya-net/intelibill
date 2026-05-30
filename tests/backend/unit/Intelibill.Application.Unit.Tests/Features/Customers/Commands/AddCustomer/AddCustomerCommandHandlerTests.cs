using Intelibill.Application.Features.Customers.Commands.AddCustomer;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Commands.AddCustomer;

public class AddCustomerCommandHandlerTests
{
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task HandleAsync_WhenValid_AddsCustomer()
    {
        var shopId = Guid.NewGuid();
        var handler = new AddCustomerCommandHandler(_customerRepository, _unitOfWork);

        var result = await handler.HandleAsync(new AddCustomerCommand(
            shopId,
            "  John Doe  ",
            "  +919876543210  ",
            "  12 Market Road  ",
            true,
            250m), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("John Doe", result.Value.Name);
        Assert.Equal("+919876543210", result.Value.PhoneNumber);
        Assert.Equal("12 Market Road", result.Value.Address);
        Assert.True(result.Value.IsActive);
        Assert.Equal(250m, result.Value.CreditLimit);

        await _customerRepository.Received(1).AddAsync(Arg.Is<Customer>(c =>
            c.ShopId == shopId
            && c.Name == "John Doe"
            && c.PhoneNumber == "+919876543210"
            && c.Address == "12 Market Road"
            && c.IsActive
            && c.CreditLimit == 250m), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
