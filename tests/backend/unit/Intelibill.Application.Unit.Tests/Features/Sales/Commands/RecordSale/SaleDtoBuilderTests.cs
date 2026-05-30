using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.RecordSale;

public class SaleDtoBuilderTests
{
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();

    private SaleDtoBuilder CreateBuilder() => new(_itemRepository);

    private static Item MakeItem(Guid shopId, string name = "Rice") =>
        Item.Create(shopId, name, "desc", "kg", $"BC-{Guid.NewGuid():N}", true, Guid.NewGuid());

    private static Sale MakeSale(Guid shopId, Guid actorId, Guid itemId, string invoiceNumber = "INV-TEST")
    {
        var line = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            itemId,
            Guid.NewGuid(),
            ServiceId: null,
            LineName: "Rice",
            LineCode: "BC-TEST",
            5m,
            80m,
            100m,
            120m,
            18m,
            false,
            false,
            PreTaxAmountBeforeDiscount: 500m,
            TaxableAmount: 500m,
            TaxAmount: 90m,
            TotalAmount: 590m);

        return Sale.Record(
            shopId,
            actorId,
            $"sale-{Guid.NewGuid():N}",
            "HASH",
            invoiceNumber,
            [line],
            null,
            null,
            null,
            PaymentMethod.Cash,
            590m,
            0m,
            DateTimeOffset.UtcNow).Value;
    }

    private static Sale MakeMixedSale(Guid shopId, Guid actorId, Guid itemId, Guid serviceId)
    {
        var goodsLine = new SaleLineInput(
            shopId,
            SaleLineType.Goods,
            itemId,
            Guid.NewGuid(),
            ServiceId: null,
            LineName: "Rice",
            LineCode: "BC-TEST",
            2m,
            80m,
            100m,
            120m,
            18m,
            false,
            false,
            PreTaxAmountBeforeDiscount: 200m,
            TaxableAmount: 200m,
            TaxAmount: 36m,
            TotalAmount: 236m);
        var serviceLine = new SaleLineInput(
            shopId,
            SaleLineType.Service,
            ItemId: null,
            InventoryBatchId: null,
            ServiceId: serviceId,
            LineName: "Consultation",
            LineCode: "SRV-100",
            Quantity: 1m,
            CostPrice: 0m,
            SalesPrice: 300m,
            Mrp: 0m,
            TaxRatePercent: 0m,
            IsPriceIncludingTax: false,
            HasPriceMismatch: false,
            TaxableAmount: 300m,
            TaxAmount: 0m,
            TotalAmount: 300m);

        return Sale.Record(
            shopId,
            actorId,
            $"sale-{Guid.NewGuid():N}",
            "HASH",
            "INV-MIXED",
            [goodsLine, serviceLine],
            null,
            null,
            null,
            PaymentMethod.Cash,
            536m,
            0m,
            DateTimeOffset.UtcNow).Value;
    }

    [Fact]
    public async Task BuildSaleDtoAsync_WhenItemExists_UsesItemName()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var item = MakeItem(shopId, "Rice");
        var sale = MakeSale(shopId, actorId, item.Id);

        _itemRepository.GetByIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([item]);

        var dto = await CreateBuilder().BuildSaleDtoAsync(sale, ["warn-1"], CancellationToken.None);

        Assert.Single(dto.Items);
        Assert.Equal("Rice", dto.Items[0].ItemName);
        Assert.Single(dto.Warnings);
        await _itemRepository.Received(1)
            .GetByIdsAsync(shopId, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == item.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildSaleDtoAsync_WhenItemMissing_UsesUnknownItemFallback()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var sale = MakeSale(shopId, actorId, Guid.NewGuid());

        _itemRepository.GetByIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var dto = await CreateBuilder().BuildSaleDtoAsync(sale, [], CancellationToken.None);

        Assert.Single(dto.Items);
        Assert.Equal("Rice", dto.Items[0].ItemName);
    }

    [Fact]
    public async Task BuildSaleDtoAsync_WhenSaleHasServiceLine_IncludesServiceInDto()
    {
        var shopId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var item = MakeItem(shopId, "Rice");
        var sale = MakeMixedSale(shopId, actorId, item.Id, serviceId);

        _itemRepository.GetByIdsAsync(shopId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([item]);

        var dto = await CreateBuilder().BuildSaleDtoAsync(sale, [], CancellationToken.None);

        Assert.Equal(2, dto.Items.Count);

        var goods = Assert.Single(dto.Items, i => i.LineType == SaleLineType.Goods);
        Assert.Equal(item.Id, goods.ItemId);
        Assert.NotNull(goods.InventoryBatchId);

        var service = Assert.Single(dto.Items, i => i.LineType == SaleLineType.Service);
        Assert.Equal(serviceId, service.ServiceId);
        Assert.Null(service.ItemId);
        Assert.Null(service.InventoryBatchId);
        Assert.Equal("Consultation", service.ItemName);
        Assert.Equal("SRV-100", service.LineCode);
    }
}
