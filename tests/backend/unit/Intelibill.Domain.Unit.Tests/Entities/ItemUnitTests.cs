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

    private static Item Unclassified(string uom, int scale = 0)
    {
        // What the migration leaves behind before the backfill reaches an item: a label, no unit code.
        var item = NewItem("Bundle", scale);
        typeof(Item).GetProperty(nameof(Item.Uom))!.SetValue(item, uom);
        return item;
    }

    [Fact]
    public void unclassified_item_saved_with_its_own_catalog_label_is_classified_despite_history()
    {
        var item = Unclassified("PCS");
        var result = item.ChangeUnit("PCS", null, new ItemStockHistory(true, 0));
        Assert.False(result.IsError);
        Assert.Equal("PCS", item.UnitCode);
        Assert.Equal(0, item.QuantityScale);
        Assert.False(item.UnitReviewRequired);
    }

    [Fact]
    public void unclassified_kg_item_takes_the_catalog_scale_when_it_covers_on_hand_stock()
    {
        var item = Unclassified("kg");
        item.MarkUnitReviewRequired();
        var result = item.ChangeUnit("KGS", null, new ItemStockHistory(true, 2));
        Assert.False(result.IsError);
        Assert.Equal(("KG", 3, "KGS", false), (item.UnitCode, item.QuantityScale, item.Uom, item.UnitReviewRequired));
    }

    [Fact]
    public void unclassified_item_relabelled_to_another_unit_with_history_is_refused()
    {
        var item = Unclassified("kg");
        var result = item.ChangeUnit("pcs", null, new ItemStockHistory(true, 0));
        Assert.True(result.IsError);
        Assert.Null(item.UnitCode);
        Assert.Equal("kg", item.Uom);
    }

    [Fact]
    public void unclassified_item_whose_catalog_scale_misses_on_hand_stock_is_refused()
    {
        var item = Unclassified("PCS");
        var result = item.ChangeUnit("PCS", null, new ItemStockHistory(true, 2));
        Assert.True(result.IsError);
        Assert.Null(item.UnitCode);
    }

    [Fact]
    public void flagged_item_saved_at_its_current_scale_clears_the_flag_when_it_covers_on_hand_stock()
    {
        var item = NewItem("Strip", 0);
        item.MarkUnitReviewRequired();
        var result = item.ChangeUnit("Strip", 0, new ItemStockHistory(true, 0));
        Assert.False(result.IsError);
        Assert.False(item.UnitReviewRequired);
    }

    [Fact]
    public void flagged_catalog_item_confirmed_at_its_current_unit_clears_the_flag()
    {
        var item = NewItem("PCS");
        item.MarkUnitReviewRequired();
        Assert.False(item.ChangeUnit("PCS", null, new ItemStockHistory(true, 0)).IsError);
        Assert.False(item.UnitReviewRequired);
    }

    [Fact]
    public void flagged_item_whose_scale_misses_on_hand_stock_keeps_the_flag()
    {
        var item = NewItem("PCS");
        item.MarkUnitReviewRequired();
        var result = item.ChangeUnit("PCS", null, new ItemStockHistory(true, 2));
        Assert.False(result.IsError);
        Assert.True(item.UnitReviewRequired);
        Assert.Equal(0, item.QuantityScale);
    }
}
