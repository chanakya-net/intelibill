using Intelibill.Application.Features.Exports.Accounting;
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
    private readonly ICreditNoteRepository _creditNoteRepository = Substitute.For<ICreditNoteRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();

    private static readonly DateOnly MayFirst = new(2026, 5, 1);
    private static readonly DateOnly MayLast = new(2026, 5, 31);

    public SalesExportDatasetBuilderTests()
    {
        _creditNoteRepository.GetByReturnIdsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<CreditNote>());
        _saleReturnRepository.GetPostedInRangeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<SaleReturn>());
        _saleRepository.GetByIdsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Sale>());
    }

    private SalesExportDatasetBuilder CreateBuilder() =>
        new(
            _saleRepository,
            _saleReturnRepository,
            _itemRepository,
            new ReturnAdjustmentReader(_saleReturnRepository, _saleRepository, _creditNoteRepository));

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
    public async Task BuildAsync_SummaryRowReturnColumns_ComeFromPostedAdjustments()
    {
        var shop = MakeShop();
        var customerId = Guid.NewGuid();
        var sale = AccountingSale(shop.Id, "INV-001", new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero), customerId);
        var creditNote = CreditNote.Issue(shop.Id, Guid.NewGuid(), 10m, "Store credit", "CN-0001", null, customerId).Value;
        var saleReturn = PostedReturn(sale, "RET-1", new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero),
            approved: 50m, taxable: 42.37m, tax: 7.63m, dueReduction: 40m, creditNoteId: creditNote.Id);
        Arrange(shop, [sale], [saleReturn]);
        _creditNoteRepository.GetByReturnIdsAsync(shop.Id, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(saleReturn.Id)), Arg.Any<CancellationToken>())
            .Returns(new List<CreditNote> { creditNote });

        var result = await CreateBuilder().BuildAsync(shop, MakeUser(), MayFirst, MayLast, SalesExportLevel.Summary, CancellationToken.None);

        var row = Assert.Single(result.SummaryRows);
        Assert.Equal("RET-1", row.ReturnNumbers);
        Assert.Equal(50m, row.ReturnAmount);
        Assert.Equal(42.37m, row.ReturnTaxableAmount);
        Assert.Equal(7.63m, row.ReturnTaxAmount);
        Assert.Equal(68m, row.NetSalesAmount);
        Assert.True(row.HasReturns);
        Assert.Equal("CN-0001", row.IssuedCreditNoteCodes);
        Assert.Equal(10m, row.IssuedCreditNoteAmount);

        var adjustment = Assert.Single(result.ReturnAdjustments);
        Assert.Equal(118m, adjustment.OriginalReturnedGross);
        Assert.Equal(40m, adjustment.SettledBy(ReturnSettlementKind.ReceivableReduction));
        Assert.Equal(10m, adjustment.SettledBy(ReturnSettlementKind.StoreCreditIssued));

        var breakup = Assert.Single(result.TaxBreakup);
        Assert.Equal((100m, 18m, 42.37m, 7.63m), (breakup.SaleTaxableAmount, breakup.SaleTaxAmount, breakup.ReturnTaxableAmount, breakup.ReturnTaxAmount));
    }

    [Fact]
    public async Task BuildAsync_ReturnPostsInItsOwnPeriod_NotWithItsSale()
    {
        var shop = MakeShop();
        var aprilSale = AccountingSale(shop.Id, "INV-APR", new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero));
        var maySale = AccountingSale(shop.Id, "INV-MAY", new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero));
        var mayReturnOfAprilSale = PostedReturn(aprilSale, "RET-APR", new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero),
            approved: 118m, taxable: 100m, tax: 18m);
        Arrange(shop, [maySale], [mayReturnOfAprilSale]);
        _saleRepository.GetByIdsAsync(shop.Id, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(aprilSale.Id)), Arg.Any<CancellationToken>())
            .Returns(new List<Sale> { aprilSale });

        var result = await CreateBuilder().BuildAsync(shop, MakeUser(), MayFirst, MayLast, SalesExportLevel.Summary, CancellationToken.None);

        var row = Assert.Single(result.SummaryRows);
        Assert.Equal("INV-MAY", row.InvoiceNumber);
        Assert.False(row.HasReturns);
        Assert.Equal(0m, row.ReturnAmount);

        var adjustment = Assert.Single(result.ReturnAdjustments);
        Assert.Equal("INV-APR", adjustment.InvoiceNumber);
        Assert.Equal(118m, adjustment.FinancialAdjustment);
        Assert.Equal(100m, Assert.Single(result.TaxBreakup).ReturnTaxableAmount);
    }

    [Fact]
    public async Task BuildAsync_ReturnAndVoidInPeriod_ShowBothAndNetToZero()
    {
        var shop = MakeShop();
        var sale = AccountingSale(shop.Id, "INV-001", new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero));
        var saleReturn = PostedReturn(sale, "RET-1", new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero),
            approved: 118m, taxable: 100m, tax: 18m);
        saleReturn.Void(new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero), Guid.NewGuid(), "Entered twice");
        Arrange(shop, [sale], [saleReturn]);

        var result = await CreateBuilder().BuildAsync(shop, MakeUser(), MayFirst, MayLast, SalesExportLevel.Summary, CancellationToken.None);

        Assert.Equal(
            [ReturnAdjustmentEntryKind.Return, ReturnAdjustmentEntryKind.VoidReversal],
            result.ReturnAdjustments.Select(a => a.Kind));
        var row = Assert.Single(result.SummaryRows);
        Assert.Equal("RET-1, RET-1 (void)", row.ReturnNumbers);
        Assert.Equal(0m, row.ReturnAmount);
        Assert.Equal(118m, row.NetSalesAmount);
        Assert.Equal(0m, Assert.Single(result.TaxBreakup).ReturnTaxAmount);
    }

    [Fact]
    public async Task BuildAsync_LineItems_ReturnedQuantityIsPhysicalAndCurrent()
    {
        var shop = MakeShop();
        var sale = AccountingSale(shop.Id, "INV-001", new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero));
        var juneReturn = PostedReturn(sale, "RET-JUN", new DateTimeOffset(2026, 6, 4, 9, 0, 0, TimeSpan.Zero),
            approved: 0m, taxable: 0m, tax: 0m);
        Arrange(shop, [sale], []);
        _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
            .Returns(new List<SaleReturn> { juneReturn });
        _itemRepository.GetByIdsAsync(shop.Id, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item>());

        var result = await CreateBuilder().BuildAsync(shop, MakeUser(), MayFirst, MayLast, SalesExportLevel.LineItems, CancellationToken.None);

        var line = Assert.Single(result.LineItemRows);
        Assert.Equal(1m, line.ReturnedQuantity);
        Assert.Equal("FullyReturned", line.ReturnStatus);
        Assert.Equal("RET-JUN", line.ReturnNumbers);
        Assert.Empty(result.ReturnAdjustments);
    }

    private void Arrange(Shop shop, IReadOnlyList<Sale> sales, IReadOnlyList<SaleReturn> postedInRange)
    {
        _saleRepository.GetByShopAndDateRangeAsync(shop.Id, MayFirst, MayLast, Arg.Any<CancellationToken>())
            .Returns(sales.ToList());
        _saleReturnRepository.GetPostedInRangeAsync(shop.Id, MayFirst, MayLast, Arg.Any<CancellationToken>())
            .Returns(postedInRange.ToList());
        foreach (var sale in sales)
        {
            _saleReturnRepository.GetBySaleAsync(shop.Id, sale.Id, Arg.Any<CancellationToken>())
                .Returns(new List<SaleReturn>());
        }
    }

    private static Sale AccountingSale(Guid shopId, string invoiceNumber, DateTimeOffset soldAt, Guid? customerId = null) =>
        Intelibill.Application.Unit.Tests.Features.Exports.Accounting.ReturnAccountingFixtures.Sale(shopId, invoiceNumber, customerId, soldAt);

    private static SaleReturn PostedReturn(
        Sale sale,
        string returnNumber,
        DateTimeOffset processedAt,
        decimal approved,
        decimal taxable,
        decimal tax,
        decimal dueReduction = 0m,
        Guid? creditNoteId = null) =>
        Intelibill.Application.Unit.Tests.Features.Exports.Accounting.ReturnAccountingFixtures.Return(
            sale,
            approved,
            taxable,
            tax,
            dueReduction: dueReduction,
            destination: creditNoteId.HasValue ? ReturnPayoutDestination.CreditNote : ReturnPayoutDestination.Refund,
            creditNoteId: creditNoteId,
            processedAt: processedAt,
            returnNumber: returnNumber);
}
