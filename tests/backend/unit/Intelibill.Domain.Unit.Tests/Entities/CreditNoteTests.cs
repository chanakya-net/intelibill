using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class CreditNoteTests
{
    [Fact]
    public void Issue_WithValidInputs_ReturnsActiveCreditNote()
    {
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var amount = 1000m;
        var reason = "Return reason";
        var code = "CN-2026-001";

        var result = CreditNote.Issue(shopId, saleId, amount, reason, code, null);

        Assert.False(result.IsError);
        var creditNote = result.Value;
        Assert.Equal(shopId, creditNote.ShopId);
        Assert.Equal(saleId, creditNote.SaleId);
        Assert.Equal(amount, creditNote.OriginalAmount);
        Assert.Equal(amount, creditNote.AvailableBalance);
        Assert.Equal(reason, creditNote.Reason);
        Assert.Equal(code, creditNote.Code);
        Assert.Equal(CreditNoteStatus.Active, creditNote.Status);
        Assert.Null(creditNote.ExpiresAt);
        Assert.Empty(creditNote.Redemptions);
    }

    [Fact]
    public void Issue_WithCustomExpiry_SetsExpirationDate()
    {
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var amount = 500m;
        var expiryDate = DateTimeOffset.UtcNow.AddDays(30);

        var result = CreditNote.Issue(shopId, saleId, amount, "reason", "CN-001", expiryDate);

        Assert.False(result.IsError);
        var creditNote = result.Value;
        Assert.Equal(expiryDate, creditNote.ExpiresAt);
        Assert.Equal(CreditNoteStatus.Active, creditNote.Status);
    }

    [Fact]
    public void Issue_WithNegativeAmount_ReturnsError()
    {
        var result = CreditNote.Issue(Guid.NewGuid(), Guid.NewGuid(), -100m, "reason", "CN-001", null);

        Assert.True(result.IsError);
    }

    [Fact]
    public void Issue_WithZeroAmount_ReturnsError()
    {
        var result = CreditNote.Issue(Guid.NewGuid(), Guid.NewGuid(), 0m, "reason", "CN-001", null);

        Assert.True(result.IsError);
    }

    [Fact]
    public void Issue_WithNullOrEmptyCode_ReturnsError()
    {
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();

        var result1 = CreditNote.Issue(shopId, saleId, 100m, "reason", null, null);
        Assert.True(result1.IsError);

        var result2 = CreditNote.Issue(shopId, saleId, 100m, "reason", "  ", null);
        Assert.True(result2.IsError);
    }

    [Fact]
    public void Redeem_WithValidAmount_ReducesBalance()
    {
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var creditNoteResult = CreditNote.Issue(shopId, saleId, 1000m, "reason", "CN-001", null);
        var creditNote = creditNoteResult.Value;

        var redemptionSaleId = Guid.NewGuid();
        var redeemResult = creditNote.Redeem(shopId, redemptionSaleId, 300m);

        Assert.False(redeemResult.IsError);
        Assert.Equal(700m, creditNote.AvailableBalance);
        Assert.Single(creditNote.Redemptions);

        var redemption = creditNote.Redemptions[0];
        Assert.Equal(300m, redemption.Amount);
        Assert.Equal(shopId, redemption.ShopId);
        Assert.Equal(redemptionSaleId, redemption.SaleId);
    }

    [Fact]
    public void Redeem_WithAmountExceedingBalance_ReturnsError()
    {
        var creditNoteResult = CreditNote.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            "reason",
            "CN-001",
            null);
        var creditNote = creditNoteResult.Value;

        var result = creditNote.Redeem(Guid.NewGuid(), Guid.NewGuid(), 600m);

        Assert.True(result.IsError);
        Assert.Equal(500m, creditNote.AvailableBalance);
    }

    [Fact]
    public void Redeem_WithZeroAmount_ReturnsError()
    {
        var creditNoteResult = CreditNote.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            "reason",
            "CN-001",
            null);
        var creditNote = creditNoteResult.Value;

        var result = creditNote.Redeem(Guid.NewGuid(), Guid.NewGuid(), 0m);

        Assert.True(result.IsError);
    }

    [Fact]
    public void Redeem_WithNegativeAmount_ReturnsError()
    {
        var creditNoteResult = CreditNote.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            "reason",
            "CN-001",
            null);
        var creditNote = creditNoteResult.Value;

        var result = creditNote.Redeem(Guid.NewGuid(), Guid.NewGuid(), -100m);

        Assert.True(result.IsError);
    }

    [Fact]
    public void Redeem_OnExpiredCreditNote_ReturnsError()
    {
        var expiredDate = DateTimeOffset.UtcNow.AddDays(-1);
        var creditNoteResult = CreditNote.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            "reason",
            "CN-001",
            expiredDate);
        var creditNote = creditNoteResult.Value;

        var result = creditNote.Redeem(Guid.NewGuid(), Guid.NewGuid(), 100m);

        Assert.True(result.IsError);
    }

    [Fact]
    public void Redeem_OnVoidedCreditNote_ReturnsError()
    {
        var creditNoteResult = CreditNote.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            "reason",
            "CN-001",
            null);
        var creditNote = creditNoteResult.Value;

        var voidResult = creditNote.Void("voided");
        Assert.False(voidResult.IsError);

        var redeemResult = creditNote.Redeem(Guid.NewGuid(), Guid.NewGuid(), 100m);
        Assert.True(redeemResult.IsError);
    }

    [Fact]
    public void Redeem_PartialThenFull_TracksMultipleRedemptions()
    {
        var shopId = Guid.NewGuid();
        var creditNoteResult = CreditNote.Issue(shopId, Guid.NewGuid(), 1000m, "reason", "CN-001", null);
        var creditNote = creditNoteResult.Value;

        var redemption1 = creditNote.Redeem(shopId, Guid.NewGuid(), 300m);
        Assert.False(redemption1.IsError);
        Assert.Equal(700m, creditNote.AvailableBalance);

        var redemption2 = creditNote.Redeem(shopId, Guid.NewGuid(), 400m);
        Assert.False(redemption2.IsError);
        Assert.Equal(300m, creditNote.AvailableBalance);

        var redemption3 = creditNote.Redeem(shopId, Guid.NewGuid(), 300m);
        Assert.False(redemption3.IsError);
        Assert.Equal(0m, creditNote.AvailableBalance);

        Assert.Equal(3, creditNote.Redemptions.Count);
    }

    [Fact]
    public void Redeem_AfterPartialRedemption_BlocksVoid()
    {
        var creditNoteResult = CreditNote.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1000m,
            "reason",
            "CN-001",
            null);
        var creditNote = creditNoteResult.Value;

        creditNote.Redeem(Guid.NewGuid(), Guid.NewGuid(), 100m);

        var voidResult = creditNote.Void("voided");
        Assert.True(voidResult.IsError);
    }

    [Fact]
    public void Void_OnActiveUnredeemedCreditNote_SucceedsAndMarksVoided()
    {
        var creditNoteResult = CreditNote.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            "reason",
            "CN-001",
            null);
        var creditNote = creditNoteResult.Value;

        var voidResult = creditNote.Void("voided reason");

        Assert.False(voidResult.IsError);
        Assert.Equal(CreditNoteStatus.Voided, creditNote.Status);
    }

    [Fact]
    public void Void_OnVoidedCreditNote_ReturnsError()
    {
        var creditNoteResult = CreditNote.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            500m,
            "reason",
            "CN-001",
            null);
        var creditNote = creditNoteResult.Value;

        creditNote.Void("first void");

        var secondVoidResult = creditNote.Void("second void");
        Assert.True(secondVoidResult.IsError);
    }

    [Fact]
    public void ComputedStatus_TracksActiveExpiredRedeemed()
    {
        var shopId = Guid.NewGuid();

        // Active
        var cn1 = CreditNote.Issue(shopId, Guid.NewGuid(), 500m, "r", "CN-001", null).Value;
        Assert.Equal(CreditNoteStatus.Active, cn1.Status);

        // Expired
        var expiredDate = DateTimeOffset.UtcNow.AddDays(-1);
        var cn2 = CreditNote.Issue(shopId, Guid.NewGuid(), 500m, "r", "CN-002", expiredDate).Value;
        Assert.Equal(CreditNoteStatus.Expired, cn2.Status);

        // Fully redeemed
        var cn3 = CreditNote.Issue(shopId, Guid.NewGuid(), 500m, "r", "CN-003", null).Value;
        cn3.Redeem(shopId, Guid.NewGuid(), 500m);
        Assert.Equal(CreditNoteStatus.FullyRedeemed, cn3.Status);
    }
}
