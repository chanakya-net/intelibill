using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Services;

public class CustomerResolverTests
{
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();

    private CustomerResolver CreateResolver() => new(_customerRepository);

    [Fact]
    public async Task ResolveAsync_WhenNoCreditSignal_ReturnsNullWithoutRepositoryCalls()
    {
        var result = await CreateResolver().ResolveAsync(
            Guid.NewGuid(),
            customerId: null,
            customerPhone: null,
            hasDueAmount: false,
            paymentMethod: PaymentMethod.Cash,
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Null(result.Value);
        await _customerRepository.DidNotReceive().GetByShopAndIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _customerRepository.DidNotReceive().GetByShopAndPhoneAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_WhenCustomerIdAndPhoneMapDifferentCustomers_ReturnsIdentityMismatch()
    {
        var shopId = Guid.NewGuid();
        var customerById = Customer.Create(shopId, "Customer A", "+911111111111", null, true);
        var customerByPhone = Customer.Create(shopId, "Customer B", "+922222222222", null, true);

        _customerRepository.GetByShopAndIdAsync(shopId, customerById.Id, Arg.Any<CancellationToken>())
            .Returns(customerById);
        _customerRepository.GetByShopAndPhoneAsync(shopId, customerByPhone.PhoneNumber!, Arg.Any<CancellationToken>())
            .Returns(customerByPhone);

        var result = await CreateResolver().ResolveAsync(
            shopId,
            customerId: customerById.Id,
            customerPhone: customerByPhone.PhoneNumber,
            hasDueAmount: true,
            paymentMethod: PaymentMethod.Credit,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.CustomerIdentityMismatch.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task ResolveAsync_WhenCreditPaymentAndNoCustomerFound_ReturnsCreditCustomerNotFound()
    {
        var shopId = Guid.NewGuid();
        _customerRepository.GetByShopAndPhoneAsync(shopId, "+919999999999", Arg.Any<CancellationToken>())
            .Returns((Customer?)null);

        var result = await CreateResolver().ResolveAsync(
            shopId,
            customerId: null,
            customerPhone: "+919999999999",
            hasDueAmount: true,
            paymentMethod: PaymentMethod.Credit,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Sale.CreditCustomerNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task ResolveAsync_WhenCustomerIdAndPhoneMatchSameCustomer_ReturnsResolvedCustomer()
    {
        var shopId = Guid.NewGuid();
        var customer = Customer.Create(shopId, "Reg User", "+911234567890", null, true);

        _customerRepository.GetByShopAndIdAsync(shopId, customer.Id, Arg.Any<CancellationToken>())
            .Returns(customer);
        _customerRepository.GetByShopAndPhoneAsync(shopId, customer.PhoneNumber!, Arg.Any<CancellationToken>())
            .Returns(customer);

        var result = await CreateResolver().ResolveAsync(
            shopId,
            customerId: customer.Id,
            customerPhone: customer.PhoneNumber,
            hasDueAmount: true,
            paymentMethod: PaymentMethod.Credit,
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value);
        Assert.Equal(customer.Id, result.Value!.Id);
    }
}
