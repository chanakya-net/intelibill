namespace Intelibill.Shared.Contracts.V1;

public static class PaymentConfirmationsV1
{
    public const string Policy = "payment-confirmation/1";
    public const string MediaType = "text/plain; charset=utf-8";
    public const string NotFound = "Billing.PaymentConfirmationNotFound";
    public const int MaxDocumentBytes = 8192 + CheckoutOrdersV1.MaxShops * (CheckoutOrdersV1.MaxShopName * 4 + 128);
    public const int MaxListBytes = 16384;
    public static string FileName(Guid id) => $"payment-confirmation-{id:D}.txt";
    public static bool IsValid(PaymentConfirmationV1? value, Guid account, Guid order) =>
        value is not null && value.ConfirmationId != Guid.Empty && value.AccountId == account && value.OrderId == order &&
        value.Source is "Online" or "Manual" && value.IssuanceBasis is "Verification" or "Retained" &&
        value.AmountPaise is > 0 and <= CheckoutOrdersV1.MaxPaise && value.Currency == "INR" &&
        EntitlementSnapshotsV1.IsInstant(value.VerifiedAt) && EntitlementSnapshotsV1.IsInstant(value.IssuedAt) &&
        (value.IssuanceBasis == "Retained" || value.IssuedAt >= value.VerifiedAt);
}

public sealed record PaymentConfirmationV1(Guid ConfirmationId, Guid AccountId, Guid OrderId, string Source,
    long AmountPaise, string Currency, DateTimeOffset VerifiedAt, DateTimeOffset IssuedAt, string IssuanceBasis);

/// <summary>Only known identity facts; the name is explicitly the billing account name at issuance.</summary>
public sealed record PaymentConfirmationPartyV1(string AccountNameAtIssuance, Guid AcceptedByMerchantUserId);
public sealed record PaymentConfirmationTermsV1(Guid PlanVersionId, int PlanVersion, string PlanName, string Period,
    int Capacity, int MembersPerShop, int ShopCount, string CustomerTermsVersion, string RefundPolicyVersion,
    string BillingTimeZone, string CalendarVersion, string RenewalPolicyVersion, string StartRule,
    DateTimeOffset AcceptedAt, int QuoteRevision, string SnapshotHash, CheckoutShopV1[] Shops);
public sealed record PaymentConfirmationSnapshotV1(string Policy, PaymentConfirmationV1 Receipt,
    PaymentConfirmationPartyV1 Party, PaymentConfirmationTermsV1 Terms, Guid ReceiptReference,
    string ProviderId, string ProviderAccountId, string Environment, string ConfigurationVersion,
    string? ProviderOrderReference, string? ProviderPaymentReference, DateTimeOffset EffectiveAt,
    string EffectiveTimeBasis, string FulfilmentAtIssuance);
