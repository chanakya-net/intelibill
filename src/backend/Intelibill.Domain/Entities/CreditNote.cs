using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class CreditNote : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SaleReturnId { get; private set; }
    public string Code { get; private set; } = null!;
    public decimal OriginalAmount { get; private set; }
    public decimal AvailableBalance { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool IsVoided { get; private set; }
    public string? VoidReason { get; private set; }

    private readonly List<CreditNoteRedemption> _redemptions = [];
    public IReadOnlyList<CreditNoteRedemption> Redemptions => _redemptions.AsReadOnly();

    private CreditNote() { }

    public static ErrorOr<CreditNote> Issue(
        Guid shopId,
        Guid saleReturnId,
        decimal amount,
        string reason,
        string? code,
        DateTimeOffset? expiresAt)
    {
        if (amount <= 0)
        {
            return Error.Validation("CreditNote.AmountMustBePositive", "Credit note amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation("CreditNote.CodeRequired", "Credit note code is required.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("CreditNote.ReasonRequired", "Reason is required.");
        }

        return new CreditNote
        {
            ShopId = shopId,
            SaleReturnId = saleReturnId,
            Code = code.Trim(),
            OriginalAmount = amount,
            AvailableBalance = amount,
            Reason = reason.Trim(),
            ExpiresAt = expiresAt,
            IsVoided = false,
        };
    }

    public ErrorOr<CreditNoteRedemption> Redeem(Guid shopId, Guid saleId, decimal amount)
    {
        if (shopId != ShopId)
        {
            return Error.Conflict("CreditNote.WrongShop", "Redemption must be made by the shop that owns this credit note.");
        }

        if (IsVoided)
        {
            return Error.Conflict("CreditNote.VoidedCannotRedeem", "Cannot redeem a voided credit note.");
        }

        if (IsExpired())
        {
            return Error.Conflict("CreditNote.ExpiredCannotRedeem", "Cannot redeem an expired credit note.");
        }

        if (amount <= 0)
        {
            return Error.Validation("CreditNote.RedemptionAmountMustBePositive", "Redemption amount must be greater than zero.");
        }

        if (amount > AvailableBalance)
        {
            return Error.Conflict("CreditNote.InsufficientBalance", $"Redemption amount {amount} exceeds available balance {AvailableBalance}.");
        }

        var result = CreditNoteRedemption.Create(Id, shopId, saleId, amount);
        if (result.IsError)
        {
            return result.Errors;
        }

        var redemption = result.Value;
        _redemptions.Add(redemption);
        AvailableBalance -= amount;

        return redemption;
    }

    public ErrorOr<Success> Void(string reason)
    {
        if (IsVoided)
        {
            return Error.Conflict("CreditNote.AlreadyVoided", "Credit note is already voided.");
        }

        if (_redemptions.Count > 0)
        {
            return Error.Conflict("CreditNote.CannotVoidAfterRedemption", "Cannot void a credit note after it has been redeemed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("CreditNote.VoidReasonRequired", "Void reason is required.");
        }

        IsVoided = true;
        VoidReason = reason.Trim();
        return Result.Success;
    }

    public CreditNoteStatus Status
    {
        get
        {
            if (IsVoided)
            {
                return CreditNoteStatus.Voided;
            }

            if (AvailableBalance == 0)
            {
                return CreditNoteStatus.FullyRedeemed;
            }

            if (IsExpired())
            {
                return CreditNoteStatus.Expired;
            }

            return CreditNoteStatus.Active;
        }
    }

    private bool IsExpired() => ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow;
}
