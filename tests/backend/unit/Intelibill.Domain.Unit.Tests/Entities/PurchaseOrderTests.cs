using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class PurchaseOrderTests
{
    [Fact]
    public void CreateDraft_SetsStatusToDraftAndNormalizes()
    {
        var shopId = Guid.NewGuid();
        var po = PurchaseOrder.CreateDraft(shopId, "PO-2026-000001", "  some notes  ");

        Assert.Equal(shopId, po.ShopId);
        Assert.Equal("PO-2026-000001", po.PurchaseOrderNumber);
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Equal("some notes", po.Notes);
        Assert.Empty(po.Lines);
    }

    [Fact]
    public void CreateDraft_WhitespaceNotes_NormalizesToNull()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", "   ");

        Assert.Null(po.Notes);
    }

    [Fact]
    public void AddLine_ValidInputs_AppendedAndTotalCalculated()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null);
        po.AddLine("  Widget A  ", 10, 25.50m);
        po.AddLine("Widget B", 5, 10m);

        Assert.Equal(2, po.Lines.Count);
        Assert.Equal("Widget A", po.Lines[0].Description);
        Assert.Equal(10, po.Lines[0].ExpectedQuantity);
        Assert.Equal(25.50m, po.Lines[0].UnitCost);
        Assert.Equal(255m, po.Lines[0].LineTotal);
        Assert.Equal(305m, po.ExpectedTotal);
    }

    [Fact]
    public void AddLine_ZeroQuantity_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => po.AddLine("Widget", 0, 10m));
    }

    [Fact]
    public void AddLine_NegativeQuantity_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => po.AddLine("Widget", -1, 10m));
    }

    [Fact]
    public void AddLine_NegativeUnitCost_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null);

        Assert.Throws<ArgumentOutOfRangeException>(() => po.AddLine("Widget", 1, -0.01m));
    }

    [Fact]
    public void AddLine_ZeroUnitCost_Allowed()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null);
        po.AddLine("Free sample", 3, 0m);

        Assert.Single(po.Lines);
        Assert.Equal(0m, po.Lines[0].LineTotal);
        Assert.Equal(0m, po.ExpectedTotal);
    }
}
