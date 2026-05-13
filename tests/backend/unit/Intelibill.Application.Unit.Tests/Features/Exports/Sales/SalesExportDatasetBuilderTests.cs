using Intelibill.Application.Features.Exports.Sales;
using Intelibill.Application.Features.Exports.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Exports.Sales;

public class SalesExportDatasetBuilderTests
{
    private readonly ISaleRepository _saleRepository = Substitute.For<ISaleRepository>();
    private readonly ISaleReturnRepository _saleReturnRepository = Substitute.For<ISaleReturnRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();

    private SalesExportDatasetBuilder CreateBuilder() =>
        new(_saleRepository, _saleReturnRepository, _itemRepository);

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 Main St", "City", "State", "560001", null, null, null);

    private static User MakeUser() =>
        User.CreateWithEmail("test@test.com", "hash", "John", "Doe");

    private static Item MakeItem(Guid shopId) =>
        Item.Create(shopId, "Test Item", "Desc", "unit", "barcode", true, Guid.NewGuid());

    [Fact]
    public async Task BuildAsync_ShouldMapSummaryRowsCorrectly()
    {
        // Arrange
        var shop = MakeShop();
        var user = MakeUser();
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var item = MakeItem(shop.Id);
        _itemRepository.GetByIdsAsync(shop.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { item });

        var saleItem = SaleItem.Create(
            shop.Id,
            item.Id,
            Guid.NewGuid(),
            2,
            100,
            150,
            200,
            18,
            true,
            false,
            taxableAmount: 254.24m,
            taxAmount: 45.76m,
            totalAmount: 300m);

        var sale = Sale.Create(
            shop.Id,
            "INV-001",
            null, "Customer", null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            300,
            0,
            300,
            45.76m,
            new List<SaleItem> { saleItem });

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, startDate, endDate, Arg.Any<CancellationToken>())
            .Returns(new List<Sale> { sale });

        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns(new List<SaleReturn>());

        var builder = CreateBuilder();

        // Act
        var result = await builder.BuildAsync(shop, user, startDate, endDate, SalesExportLevel.Summary, CancellationToken.None);

        // Assert
        Assert.Single(result.SummaryRows);
        var row = result.SummaryRows[0];
        Assert.Equal("INV-001", row.InvoiceNumber);
        Assert.Equal(300, row.TotalAmount);
        Assert.Equal(0, row.ReturnAmount);
        Assert.Equal(300, row.NetSalesAmount);
        Assert.False(row.HasReturns);
    }

    [Fact]
    public async Task BuildAsync_ShouldIncludeReturnsInSummaryAndTaxBreakup()
    {
        // Arrange
        var shop = MakeShop();
        var user = MakeUser();
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var item = MakeItem(shop.Id);
        _itemRepository.GetByIdsAsync(shop.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { item });

        var saleItem = SaleItem.Create(
            shop.Id,
            item.Id,
            Guid.NewGuid(),
            2,
            100,
            150,
            200,
            18,
            true,
            false,
            taxableAmount: 254.24m,
            taxAmount: 45.76m,
            totalAmount: 300m);

        var sale = Sale.Create(
            shop.Id,
            "INV-001",
            null, "Customer", null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            300,
            0,
            300,
            45.76m,
            new List<SaleItem> { saleItem });

        var returnLine = new SaleReturnLineInput(
            shop.Id,
            saleItem.Id,
            1,
            SaleReturnCondition.Restockable,
            100,
            150,
            18,
            true,
            150,
            150,
            127.12m,
            22.88m,
            null);

        var saleReturn = SaleReturn.Record(
            shop.Id,
            sale.Id,
            "RET-001",
            DateTimeOffset.UtcNow,
            user.Id,
            null,
            150,
            0,
            150,
            PaymentMethod.Cash,
            127.12m,
            22.88m,
            null,
            null,
            new List<SaleReturnLineInput> { returnLine }).Value;

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, startDate, endDate, Arg.Any<CancellationToken>())
            .Returns(new List<Sale> { sale });

        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns(new List<SaleReturn> { saleReturn });

        var builder = CreateBuilder();

        // Act
        var result = await builder.BuildAsync(shop, user, startDate, endDate, SalesExportLevel.Summary, CancellationToken.None);

        // Assert
        Assert.Single(result.SummaryRows);
        var row = result.SummaryRows[0];
        Assert.Equal(300, row.TotalAmount);
        Assert.Equal(150, row.ReturnAmount);
        Assert.Equal(150, row.NetSalesAmount);
        Assert.True(row.HasReturns);
        Assert.Equal("RET-001", row.ReturnNumbers);

        Assert.Single(result.TaxBreakup);
        var tax = result.TaxBreakup[0];
        Assert.Equal(18, tax.TaxRatePercent);
        Assert.Equal(254.24m, tax.SaleTaxableAmount);
        Assert.Equal(127.12m, tax.ReturnTaxableAmount);
        Assert.Equal(127.12m, tax.NetTaxableAmount);
    }

    [Fact]
    public async Task BuildAsync_ShouldExcludeVoidedReturns()
    {
        // Arrange
        var shop = MakeShop();
        var user = MakeUser();
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var sale = Sale.Create(
            shop.Id, "INV-001", null, null, null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 100, 0, 100, 0, new List<SaleItem>());

        var saleReturn = SaleReturn.Record(
            shop.Id, sale.Id, "RET-001", DateTimeOffset.UtcNow, user.Id, null, 100, 0, 100, PaymentMethod.Cash, 100, 0, null, null, new List<SaleReturnLineInput>()).Value;
        saleReturn.Void(DateTimeOffset.UtcNow, user.Id, "Testing");

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, startDate, endDate, Arg.Any<CancellationToken>())
            .Returns(new List<Sale> { sale });

        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns(new List<SaleReturn> { saleReturn });

        var builder = CreateBuilder();

        // Act
        var result = await builder.BuildAsync(shop, user, startDate, endDate, SalesExportLevel.Summary, CancellationToken.None);

        // Assert
        var row = result.SummaryRows[0];
        Assert.False(row.HasReturns);
        Assert.Equal(0, row.ReturnAmount);
    }
}
