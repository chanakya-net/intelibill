using Intelibill.Application.Features.Customers.Queries.GetCustomers;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Customers.Queries.GetCustomers;

public class GetCustomersQueryHandlerTests
{
    private readonly ICustomerRepository _customerRepository = Substitute.For<ICustomerRepository>();
    private readonly ICustomerLedgerEntryRepository _customerLedgerEntryRepository = Substitute.For<ICustomerLedgerEntryRepository>();
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly GetCustomersQueryHandler _handler;

    public GetCustomersQueryHandlerTests()
    {
        _handler = new GetCustomersQueryHandler(_customerRepository, _customerLedgerEntryRepository, _saleRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenNoCustomers_ReturnsEmptyList()
    {
        var shopId = Guid.NewGuid();
        _customerRepository.GetByShopIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Customer>());
        _saleRepository.GetCustomerSalesMetricsAsync(shopId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, CustomerSalesMetricsReadModel>());

        var result = await _handler.HandleAsync(new GetCustomersQuery(shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomersExist_ReturnsMappedDtosWithZeroDefaultsForMissingMetrics()
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
        _saleRepository.GetCustomerSalesMetricsAsync(shopId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, CustomerSalesMetricsReadModel>());

        var result = await _handler.HandleAsync(new GetCustomersQuery(shopId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Count);

        var aliceDto = result.Value.Single(d => d.CustomerId == c1.Id);
        Assert.Equal("Alice", aliceDto.Name);
        Assert.Equal(15m, aliceDto.OutstandingDue);
        Assert.Equal(0, aliceDto.PurchaseCount);
        Assert.Equal(0m, aliceDto.LifetimeRevenue);
        Assert.Equal(0m, aliceDto.CurrentMonthRevenue);

        var bobDto = result.Value.Single(d => d.CustomerId == c2.Id);
        Assert.Equal("Bob", bobDto.Name);
        Assert.Equal(0m, bobDto.OutstandingDue);
        Assert.Equal(0, bobDto.PurchaseCount);
        Assert.Equal(0m, bobDto.LifetimeRevenue);
        Assert.Equal(0m, bobDto.CurrentMonthRevenue);
    }

    [Fact]
    public async Task HandleAsync_WhenMetricsExist_MapsMetricsCorrectly()
    {
        var shopId = Guid.NewGuid();
        var c1 = Customer.Create(shopId, "Alice", "+919000000001", "Addr1");
        _customerRepository.GetByShopIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(new[] { c1 });
        _customerLedgerEntryRepository.GetCustomerBalancesAsync(shopId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [c1.Id] = 15m });
        _saleRepository.GetCustomerSalesMetricsAsync(shopId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, CustomerSalesMetricsReadModel>
            {
                [c1.Id] = new CustomerSalesMetricsReadModel(c1.Id, 5, 500m, 120m)
            });

        var result = await _handler.HandleAsync(new GetCustomersQuery(shopId), CancellationToken.None);

        Assert.False(result.IsError);
        var aliceDto = Assert.Single(result.Value);
        Assert.Equal(5, aliceDto.PurchaseCount);
        Assert.Equal(500m, aliceDto.LifetimeRevenue);
        Assert.Equal(120m, aliceDto.CurrentMonthRevenue);
    }

    [Fact]
    public async Task HandleAsync_QueriesSalesMetricsWithExpectedCurrentMonthUtcBoundaries()
    {
        var shopId = Guid.NewGuid();
        var c1 = Customer.Create(shopId, "Alice", "+919000000001", "Addr1");
        _customerRepository.GetByShopIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(new[] { c1 });
        _customerLedgerEntryRepository.GetCustomerBalancesAsync(shopId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [c1.Id] = 0m });
        _saleRepository.GetCustomerSalesMetricsAsync(shopId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, CustomerSalesMetricsReadModel>());

        var result = await _handler.HandleAsync(new GetCustomersQuery(shopId), CancellationToken.None);

        var todayUtc = DateTimeOffset.UtcNow.UtcDateTime;
        var expectedMonthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedNextMonthStart = expectedMonthStart.AddMonths(1);

        await _saleRepository.Received(1).GetCustomerSalesMetricsAsync(
            shopId,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(c1.Id)),
            Arg.Is<DateTime>(dt => dt == expectedMonthStart),
            Arg.Is<DateTime>(dt => dt == expectedNextMonthStart),
            Arg.Any<CancellationToken>());
    }
}
