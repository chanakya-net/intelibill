using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class PurchaseOrderTests
{
    [Fact]
    public void CreateDraft_SetsStatusToDraftAndNormalizes()
    {
        var shopId = Guid.NewGuid();
        var po = PurchaseOrder.CreateDraft(shopId, "PO-2026-000001", null, null, null, null, "  some notes  ");

        Assert.Equal(shopId, po.ShopId);
        Assert.Equal("PO-2026-000001", po.PurchaseOrderNumber);
        Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        Assert.Equal("some notes", po.Notes);
        Assert.Empty(po.Lines);
    }

    [Fact]
    public void CreateDraft_WhitespaceNotes_NormalizesToNull()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, "   ");

        Assert.Null(po.Notes);
    }

    [Fact]
    public void AddLine_ValidInputs_AppendedAndTotalCalculated()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "  Widget A  ", 10, 25.50m);
        po.AddLine(Guid.NewGuid(), "Widget B", 5, 10m);

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
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);

        Assert.Throws<ArgumentOutOfRangeException>(() => po.AddLine(Guid.NewGuid(), "Widget", 0, 10m));
    }

    [Fact]
    public void AddLine_NegativeQuantity_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);

        Assert.Throws<ArgumentOutOfRangeException>(() => po.AddLine(Guid.NewGuid(), "Widget", -1, 10m));
    }

    [Fact]
    public void AddLine_NegativeUnitCost_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);

        Assert.Throws<ArgumentOutOfRangeException>(() => po.AddLine(Guid.NewGuid(), "Widget", 1, -0.01m));
    }

    [Fact]
    public void AddLine_ZeroUnitCost_Allowed()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Free sample", 3, 0m);

        Assert.Single(po.Lines);
        Assert.Equal(0m, po.Lines[0].LineTotal);
        Assert.Equal(0m, po.ExpectedTotal);
    }

    // ---- Place ----

    [Fact]
    public void Place_DraftWithLines_SetsStatusToPlacedAndSetsSupplier()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m);
        var supplierId = Guid.NewGuid();

        po.Place(supplierId);

        Assert.Equal(PurchaseOrderStatus.Placed, po.Status);
        Assert.Equal(supplierId, po.SupplierId);
    }

    [Fact]
    public void Place_DraftWithNoLines_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);

        Assert.Throws<InvalidOperationException>(() => po.Place(Guid.NewGuid()));
    }

    [Fact]
    public void Place_AlreadyPlaced_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m);
        po.Place(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => po.Place(Guid.NewGuid()));
    }

    // ---- CanDeleteDraft ----

    [Fact]
    public void CanDeleteDraft_WhenDraft_ReturnsTrue()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        Assert.True(po.CanDeleteDraft);
    }

    [Fact]
    public void CanDeleteDraft_WhenPlaced_ReturnsFalse()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 1, 10m);
        po.Place(Guid.NewGuid());

        Assert.False(po.CanDeleteDraft);
    }

    // ---- UpdateDraft immutability ----

    [Fact]
    public void UpdateDraft_WhenPlaced_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 1, 10m);
        po.Place(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => po.UpdateDraft(null, null, null, null, null, []));
    }

    // ---- Cancel ----

    [Fact]
    public void Cancel_Draft_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);

        Assert.Throws<InvalidOperationException>(() => po.Cancel("Ordered by mistake"));
    }

    [Fact]
    public void Cancel_PlacedWithZeroReceived_SetsCancelledStatus()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m);
        po.Place(Guid.NewGuid());

        po.Cancel("Supplier unavailable");

        Assert.Equal(PurchaseOrderStatus.Cancelled, po.Status);
        Assert.Equal("Supplier unavailable", po.CancellationReason);
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m);
        po.Place(Guid.NewGuid());
        po.Cancel("First reason");

        Assert.Throws<InvalidOperationException>(() => po.Cancel("Second reason"));
    }

    [Fact]
    public void Cancel_WithEmptyReason_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m);
        po.Place(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => po.Cancel("   "));
    }

    [Fact]
    public void Cancel_WithReceivedItems_Throws()
    {
        var po = PurchaseOrder.CreateDraft(Guid.NewGuid(), "PO-2026-000001", null, null, null, null, null);
        po.AddLine(Guid.NewGuid(), "Widget", 5, 10m, receivedQuantity: 2);
        po.Place(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => po.Cancel("Too late"));
    }
}
