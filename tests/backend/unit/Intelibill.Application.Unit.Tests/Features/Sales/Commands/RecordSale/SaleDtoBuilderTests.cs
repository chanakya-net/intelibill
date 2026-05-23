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
            itemId,
            Guid.NewGuid(),
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
        Assert.Equal("Unknown Item", dto.Items[0].ItemName);
    }
}
