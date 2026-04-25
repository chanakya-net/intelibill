using Intelibill.Application.Features.Customers.Queries.GetCustomers;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Queries.GetCustomers;

public class GetCustomersQueryHandlerTests
{
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly GetCustomersQueryHandler _handler;

    public GetCustomersQueryHandlerTests()
    {
        _handler = new GetCustomersQueryHandler(_customerRepository, _customerLedgerEntryRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenNoCustomers_ReturnsEmptyList()
    {
        var shopId = Guid.NewGuid();
        _customerRepository.GetByShopIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Customer>());

        var result = await _handler.HandleAsync(new GetCustomersQuery(shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomersExist_ReturnsMappedDtos()
    {
        var shopId = Guid.NewGuid();
        var c1 = Customer.Create(shopId, "Alice", "+919000000001", "Addr1");
        var c2 = Customer.Create(shopId, "Bob", "+919000000002", null);
        _customerRepository.GetByShopIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(new[] { c1, c2 });
        _customerLedgerEntryRepository.GetCustomerBalancesAsync(shopId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>
            {
                [c1.Id] = 15m,
                [c2.Id] = 0m,
            });

        var result = await _handler.HandleAsync(new GetCustomersQuery(shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, d => d.Name == "Alice" && d.Address == "Addr1" && d.OutstandingDue == 15m);
        Assert.Contains(result.Value, d => d.Name == "Bob" && d.Address == null && d.OutstandingDue == 0m);
    }
}
