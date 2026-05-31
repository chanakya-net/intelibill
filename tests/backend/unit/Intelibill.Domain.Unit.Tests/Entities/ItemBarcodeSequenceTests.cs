using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class ItemBarcodeSequenceTests
{
    [Fact]
    public void Create_InitialCode_IsFormatted()
    {
        var sequence = ItemBarcodeSequence.Create(Guid.NewGuid());

        var first = sequence.NextCode();

        Assert.Equal("IB-000001", first);
        Assert.Equal(2, sequence.NextNumber);
    }

    [Fact]
    public void NextCode_SequentialCalls_IncrementNumber()
    {
        var sequence = ItemBarcodeSequence.Create(Guid.NewGuid());

        var first = sequence.NextCode();
        var second = sequence.NextCode();
        var third = sequence.NextCode();

        Assert.Equal("IB-000001", first);
        Assert.Equal("IB-000002", second);
        Assert.Equal("IB-000003", third);
        Assert.Equal(4, sequence.NextNumber);
    }

    [Fact]
    public void NextCode_UsesPrefixProperty_AndNumberPadding()
    {
        var sequence = ItemBarcodeSequence.Create(Guid.NewGuid());
        var first = sequence.NextCode();

        Assert.Matches("^IB-\\d{6}$", first);
        Assert.Equal(2, sequence.NextNumber);
    }
}
