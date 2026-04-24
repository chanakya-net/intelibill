using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Events;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class SupplierLedgerEntryTests
{
    [Fact]
    public void Create_GoodsReceived_WithBatchAndPositiveAmount_Succeeds()
    {
        var result = SupplierLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SupplierLedgerEntryType.GoodsReceived,
            1500m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "  Initial goods received  ",
            Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Equal(1500m, result.Value.Amount);
        Assert.NotNull(result.Value.BatchId);
        Assert.Equal("Initial goods received", result.Value.Notes);
    }

    [Fact]
    public void Create_RecordAdjusted_WithNegativeAmount_Succeeds()
    {
        var result = SupplierLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            SupplierLedgerEntryType.RecordAdjusted,
            -500m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Equal(-500m, result.Value.Amount);
    }

    [Fact]
    public void Create_WithZeroAmount_ReturnsValidationError()
    {
        var result = SupplierLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SupplierLedgerEntryType.GoodsReceived,
            0m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("SupplierLedgerEntry.AmountZero", result.FirstError.Code);
    }

    [Fact]
    public void Create_PaymentMade_WithNullBatchAndPositiveAmount_Succeeds()
    {
        var result = SupplierLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            SupplierLedgerEntryType.PaymentMade,
            500m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "bank transfer",
            Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Equal(500m, result.Value.Amount);
        Assert.Null(result.Value.BatchId);
        Assert.Equal("bank transfer", result.Value.Notes);
    }

    [Fact]
    public void Create_PaymentMade_WithBatchId_ReturnsValidationError()
    {
        var result = SupplierLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SupplierLedgerEntryType.PaymentMade,
            500m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            Guid.NewGuid());

        Assert.True(result.IsError);
        Assert.Equal("SupplierLedgerEntry.BatchNotAllowed", result.FirstError.Code);
    }

    [Fact]
    public void Create_PaymentMade_WithWhitespaceNotes_NormalizesToNull()
    {
        var result = SupplierLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            SupplierLedgerEntryType.PaymentMade,
            250m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "   ",
            Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Null(result.Value.Notes);
    }

    [Fact]
    public void Create_WithPaymentMade_RaisesNoDomainEvents()
    {
        // Expense creation is handled atomically inside MakeSupplierPaymentCommandHandler,
        // not via a domain event, to avoid post-commit drift on accounting-critical writes.
        var result = SupplierLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            SupplierLedgerEntryType.PaymentMade,
            500m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "bank transfer",
            Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Empty(result.Value.DomainEvents);
    }

    [Fact]
    public void Create_WithGoodsReceived_DoesNotRaiseEvent()
    {
        var result = SupplierLedgerEntry.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SupplierLedgerEntryType.GoodsReceived,
            1500m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            Guid.NewGuid());

        Assert.False(result.IsError);
        Assert.Empty(result.Value.DomainEvents);
    }
}
