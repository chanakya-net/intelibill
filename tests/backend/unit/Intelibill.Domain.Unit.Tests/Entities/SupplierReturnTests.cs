using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public sealed class SupplierReturnTests
{
    private static SupplierReturn Create() => SupplierReturn.Create(Guid.NewGuid(), Guid.NewGuid(), "SR-1", "Damaged goods", Guid.NewGuid(), DateTimeOffset.UtcNow, new DateOnly(2026,11,7), "Asia/Kolkata", "Shop", "Supplier").Value;

    [Fact]
    public void PendingShipment_HasNoCredit()
    {
        var shipment = Create();
        Assert.Equal(SupplierReturnStatus.PendingSupplierCredit, shipment.Status);
        Assert.Null(shipment.Credit);
    }

    [Fact]
    public void AcceptCredit_OnceOnly()
    {
        var shipment = Create();
        Assert.False(shipment.AcceptCredit().IsError);
        Assert.Equal(SupplierReturnStatus.CreditAccepted, shipment.Status);
        Assert.Equal("SupplierReturn.CreditAlreadyAccepted", shipment.AcceptCredit().FirstError.Code);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, -1)]
    [InlineData(1, 0.001)]
    [InlineData(1, 100000000)]
    public void InvalidQuantityMoneyReason_Refused(decimal quantity, decimal proposed)
    {
        var line = SupplierReturnLine.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, quantity, proposed, 10, 100, "Item", "NOS", "Receipt", Guid.NewGuid());
        Assert.True(line.IsError);
    }

    [Fact]
    public void ZeroCredit_HasEvidenceWithoutLedger()
    {
        var result = SupplierReturnCreditLine.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, null);
        Assert.False(result.IsError);
        Assert.Null(result.Value.SupplierLedgerEntryId);
        Assert.True(SupplierReturnCreditLine.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid()).IsError);
    }
}
