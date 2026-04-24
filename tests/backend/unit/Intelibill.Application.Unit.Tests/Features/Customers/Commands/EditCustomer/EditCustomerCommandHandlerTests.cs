using ErrorOr;
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
    public async Task HandleAsync_WhenCustomerNotFound_ReturnsNotFoundError()
    {
        _customerRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var command = new EditCustomerCommand(
            Guid.NewGuid(), Guid.NewGuid(),
            "New Name", "+919876543210", null, true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerBelongsToDifferentOwner_ReturnsNotFoundError()
    {
        var customer = Customer.Create(Guid.NewGuid(), "Old Name", "+911234567890", null);
        _customerRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(customer);

        var command = new EditCustomerCommand(
            Guid.NewGuid(), customer.Id,
            "New Name", "+919876543210", null, true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_UpdatesCustomerAndReturnsDto()
    {
        var ownerId = Guid.NewGuid();
        var customer = Customer.Create(ownerId, "Old Name", "+911234567890", null);
        _customerRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(customer);

        var command = new EditCustomerCommand(
            ownerId, customer.Id,
            "New Name", "+919999999999", "12 MG Road", false);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("New Name", result.Value.Name);
        Assert.Equal("+919999999999", result.Value.PhoneNumber);
        Assert.Equal("12 MG Road", result.Value.Address);
        Assert.False(result.Value.IsActive);
        _customerRepository.Received(1).Update(customer);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
