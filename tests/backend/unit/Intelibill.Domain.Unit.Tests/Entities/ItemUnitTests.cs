using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class ItemUnitTests
{
    private static Item NewItem(string uom, int? scale = null) =>
        Item.Create(Guid.NewGuid(), "Thing", null, uom, "BAR", true, Guid.NewGuid(), quantityScale: scale);

    [Fact]
    public void change_unit_rejects_out_of_range_scale_for_custom_unit()
    {
        var item = NewItem("Bundle", 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => item.ChangeUnit("Bundle", 4, ItemStockHistory.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.ChangeUnit("Bundle", -1, ItemStockHistory.None));
    }

    [Fact]
    public void kg_item_has_scale_three_and_kgs_document_unit()
    {
        var item = NewItem("kg");
        Assert.Equal("KG", item.UnitCode);
        Assert.Equal(3, item.QuantityScale);
        Assert.Equal("KGS", item.DocumentUnit);
        Assert.False(item.UnitReviewRequired);
    }

    [Fact]
    public void custom_label_keeps_requested_scale()
    {
        var item = NewItem("Baggie", 2);
        Assert.Null(item.UnitCode);
        Assert.Equal(2, item.QuantityScale);
        Assert.Equal("OTH", item.DocumentUnit);
        Assert.Equal(0, NewItem("Baggie").QuantityScale);
        Assert.Equal("NOS", NewItem("Baggie").DocumentUnit);
    }

    [Fact]
    public void unit_change_with_history_is_rejected()
    {
        var item = NewItem("kg");
        var result = item.ChangeUnit("pcs", null, new ItemStockHistory(true, 3));
        Assert.True(result.IsError);
        Assert.Equal("Item.UnitChangeWithHistory", result.FirstError.Code);
        Assert.Equal("kg", item.Uom);
    }

    [Fact]
    public void relabel_same_unit_with_history_is_allowed()
    {
        var item = NewItem("kg");
        var result = item.ChangeUnit("KGS", null, new ItemStockHistory(true, 3));
        Assert.False(result.IsError);
        Assert.Equal("KGS", item.Uom);
        Assert.Equal(3, item.QuantityScale);
    }

    [Fact]
    public void review_item_can_be_classified_to_covering_scale()
    {
        var review = NewItem("odd");
        review.MarkUnitReviewRequired();
        Assert.True(review.UnitReviewRequired);
        Assert.True(review.ChangeUnit("pcs", null, new ItemStockHistory(true, 2)).IsError);
        Assert.True(review.UnitReviewRequired);
        Assert.False(review.ChangeUnit("kg", null, new ItemStockHistory(true, 2)).IsError);
        Assert.False(review.UnitReviewRequired);
        Assert.Equal(3, review.QuantityScale);
    }
}
