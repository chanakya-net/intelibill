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

        var saleItem = SaleItem.CreateGoods(
            shop.Id,
            item.Id,
            Guid.NewGuid(),
            lineName: item.Name,
            lineCode: item.Barcode,
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

        // Metadata assertions
        Assert.Equal(shop.Name, result.Metadata.ShopName);
        Assert.Contains(shop.Address, result.Metadata.ShopAddress);
        Assert.Equal($"{user.FirstName} {user.LastName}", result.Metadata.GeneratedBy);
        Assert.Equal(startDate, result.Metadata.StartDate);
        Assert.Equal(endDate, result.Metadata.EndDate);
        Assert.Equal(SalesExportLevel.Summary, result.Metadata.ExportLevel);
    }

    [Fact]
    public async Task BuildAsync_ShouldMapLineItemsCorrectly()
    {
        // Arrange
        var shop = MakeShop();
        var user = MakeUser();
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var item = MakeItem(shop.Id);
        _itemRepository.GetByIdsAsync(shop.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { item });

        var saleItem = SaleItem.CreateGoods(
            shop.Id,
            item.Id,
            Guid.NewGuid(),
            lineName: item.Name,
            lineCode: item.Barcode,
            2,
            100,
            150,
            200,
            18,
            true,
            false,
            itemDiscountAmount: 150m,
            saleDiscountAmount: 50m,
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
            ReturnPayoutDestination.Refund,
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
        var result = await builder.BuildAsync(shop, user, startDate, endDate, SalesExportLevel.LineItems, CancellationToken.None);

        // Assert
        Assert.Single(result.LineItemRows);
        var row = result.LineItemRows[0];
        Assert.Equal("INV-001", row.InvoiceNumber);
        Assert.Equal("Customer", row.CustomerName);
        Assert.Equal("Test Item", row.ItemName);
        Assert.Equal(2, row.SalesQuantity);
        Assert.Equal(150, row.SalesPrice);
        Assert.Equal(150m, row.ItemDiscountAmount);
        Assert.Equal(50m, row.SaleDiscountAmount);
        Assert.Equal(18, row.TaxRatePercent);
        Assert.Equal(254.24m, row.TaxableAmount);
        Assert.Equal(45.76m, row.TaxAmount);
        Assert.Equal(300, row.LineTotal);
        Assert.True(row.IsPriceIncludingTax);
        Assert.Equal(1, row.ReturnedQuantity);
        Assert.Equal("PartiallyReturned", row.ReturnStatus);
        Assert.Equal("RET-001", row.ReturnNumbers);
        Assert.Equal(SaleLineType.Goods, row.LineType);
    }

    [Fact]
    public async Task BuildAsync_LineItems_IncludesServiceLineTypeAndSnapshotName()
    {
        var shop = MakeShop();
        var user = MakeUser();
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        _itemRepository.GetByIdsAsync(shop.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item>());

        var serviceItem = SaleItem.CreateService(
            shop.Id,
            Guid.NewGuid(),
            lineName: "AC Repair",
            lineCode: "SAC-9987",
            quantity: 1,
            costPrice: 0,
            salesPrice: 500,
            mrp: 500,
            taxRatePercent: 18,
            isPriceIncludingTax: true,
            hasPriceMismatch: false,
            taxableAmount: 423.73m,
            taxAmount: 76.27m,
            totalAmount: 500m);

        var sale = Sale.Create(
            shop.Id,
            "INV-SRV-001",
            null,
            "Customer",
            null,
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            500,
            0,
            500,
            76.27m,
            new List<SaleItem> { serviceItem });

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, startDate, endDate, Arg.Any<CancellationToken>())
            .Returns(new List<Sale> { sale });
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns(new List<SaleReturn>());

        var builder = CreateBuilder();
        var result = await builder.BuildAsync(shop, user, startDate, endDate, SalesExportLevel.LineItems, CancellationToken.None);

        Assert.Single(result.LineItemRows);
        var row = result.LineItemRows[0];
        Assert.Equal("AC Repair", row.ItemName);
        Assert.Equal(SaleLineType.Service, row.LineType);
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

        var saleItem = SaleItem.CreateGoods(
            shop.Id,
            item.Id,
            Guid.NewGuid(),
            lineName: item.Name,
            lineCode: item.Barcode,
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
            ReturnPayoutDestination.Refund,
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
    public async Task BuildAsync_ShouldPopulateReturnRowsCorrectly()
    {
        // Arrange
        var shop = MakeShop();
        var user = MakeUser();
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var sale = Sale.Create(
            shop.Id, "INV-001", null, "Alice", null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 300, 0, 300, 45.76m, new List<SaleItem>());

        var returnLine = new SaleReturnLineInput(
            shop.Id, Guid.NewGuid(), 1, SaleReturnCondition.Restockable, 100, 150, 18, true, 150, 150, 127.12m, 22.88m, null);

        var saleReturn = SaleReturn.Record(
            shop.Id, sale.Id, "RET-001", DateTimeOffset.UtcNow, user.Id, null, 150, 0, 150, ReturnPayoutDestination.Refund, 127.12m, 22.88m, null, null, new List<SaleReturnLineInput> { returnLine }).Value;

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, startDate, endDate, Arg.Any<CancellationToken>())
            .Returns(new List<Sale> { sale });

        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns(new List<SaleReturn> { saleReturn });

        var builder = CreateBuilder();

        // Act
        var result = await builder.BuildAsync(shop, user, startDate, endDate, SalesExportLevel.Summary, CancellationToken.None);

        // Assert
        Assert.Single(result.ReturnRows);
        var row = result.ReturnRows[0];
        Assert.Equal("RET-001", row.ReturnNumber);
        Assert.Equal("INV-001", row.InvoiceNumber);
        Assert.Equal("Alice", row.CustomerName);
        Assert.Equal(150, row.TotalRefundAmount);
        Assert.Equal(127.12m, row.TotalTaxableAmount);
        Assert.Equal(22.88m, row.TotalTaxAmount);
        
        Assert.Single(row.TaxBreakup);
        var tax = row.TaxBreakup[0];
        Assert.Equal(18, tax.TaxRatePercent);
        Assert.Equal(127.12m, tax.TaxableAmount);
        Assert.Equal(22.88m, tax.TaxAmount);
    }

    [Fact]
    public async Task BuildAsync_ShouldIncludeVoidedReturnRowsMarkedAsVoided()
    {
        // Arrange
        var shop = MakeShop();
        var user = MakeUser();
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var endDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var sale = Sale.Create(
            shop.Id, "INV-001", null, null, null, PaymentMethod.Cash, DateTimeOffset.UtcNow, 100, 0, 100, 0, new List<SaleItem>());

        var saleReturn = SaleReturn.Record(
            shop.Id, sale.Id, "RET-001", DateTimeOffset.UtcNow, user.Id, null, 100, 0, 100, ReturnPayoutDestination.Refund, 100, 0, null, null, new List<SaleReturnLineInput>()).Value;
        saleReturn.Void(DateTimeOffset.UtcNow, user.Id, "Testing");

        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, startDate, endDate, Arg.Any<CancellationToken>())
            .Returns(new List<Sale> { sale });

        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns(new List<SaleReturn> { saleReturn });

        var builder = CreateBuilder();

        // Act
        var result = await builder.BuildAsync(shop, user, startDate, endDate, SalesExportLevel.Summary, CancellationToken.None);

        // Assert - voided return should be included but marked as voided
        Assert.Single(result.ReturnRows);
        Assert.True(result.ReturnRows[0].IsVoided);
        Assert.Equal("RET-001", result.ReturnRows[0].ReturnNumber);
    }
}
