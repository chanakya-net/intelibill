using System.Globalization;
using System.Text;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

/// <summary>Versioned renderer: no live party, catalogue, access or term lookups.</summary>
public static class PaymentConfirmationBuilder
{
    public static byte[] Render(PaymentConfirmationSnapshotV1 snapshot)
    {
        if (snapshot.Policy != PaymentConfirmationsV1.Policy)
            throw new InvalidOperationException("Unsupported payment confirmation version.");
        var r = snapshot.Receipt;
        var lines = new List<string>
        {
            "PAYMENT CONFIRMATION",
            "This is not a tax invoice, not a GST document and not a merchant sale receipt.",
            "Verified receipt only; this document does not prove applied access.",
            $"Confirmation: {r.ConfirmationId:D}", $"Billing account: {r.AccountId:D}", $"Order: {r.OrderId:D}",
            $"Source: {r.Source}", $"Receipt reference: {snapshot.ReceiptReference:D}",
            $"Gross amount: {(r.AmountPaise / 100m).ToString("F2", CultureInfo.InvariantCulture)} {r.Currency}",
            $"Gross amount (paise): {r.AmountPaise.ToString(CultureInfo.InvariantCulture)} {r.Currency}",
            $"Verified receipt: {Instant(r.VerifiedAt)}", $"Effective receipt: {Instant(snapshot.EffectiveAt)}",
            $"Effective time basis: {snapshot.EffectiveTimeBasis}", $"Issued: {Instant(r.IssuedAt)}",
            $"Issuance basis: {r.IssuanceBasis}",
            "Party details below were recorded at confirmation issuance.",
            $"Billing account name at issuance: {Text(snapshot.Party.AccountNameAtIssuance)}",
            $"Accepted by merchant user ID: {snapshot.Party.AcceptedByMerchantUserId:D}",
            "Legal party name, address and tax identity: not recorded by this confirmation.",
            $"Provider: {Text(snapshot.ProviderId)} / {Text(snapshot.ProviderAccountId)} / {Text(snapshot.Environment)}",
            $"Provider configuration: {Text(snapshot.ConfigurationVersion)}",
            $"Provider order reference: {Text(snapshot.ProviderOrderReference ?? "Not recorded")}",
            $"Provider payment reference: {Text(snapshot.ProviderPaymentReference ?? "Not disclosed; manual receipt reference above")}",
            $"Fulfilment at issuance (historical): {snapshot.FulfilmentAtIssuance}",
        };
        Terms(lines, snapshot.Terms);
        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }

    private static void Terms(List<string> lines, PaymentConfirmationTermsV1 t)
    {
        lines.Add($"Purchased plan: {Text(t.PlanName)} / version {t.PlanVersion.ToString(CultureInfo.InvariantCulture)} / {t.PlanVersionId:D}");
        lines.Add($"Purchased period: {t.Period}; start rule: {t.StartRule}");
        lines.Add(FormattableString.Invariant($"Capacity: {t.Capacity}; named shops: {t.ShopCount}; members per shop: {t.MembersPerShop}"));
        lines.Add($"Accepted: {Instant(t.AcceptedAt)}; quote revision: {t.QuoteRevision.ToString(CultureInfo.InvariantCulture)}");
        lines.Add($"Accepted snapshot: {t.SnapshotHash}");
        lines.Add($"Policies: terms {Text(t.CustomerTermsVersion)}; refund {Text(t.RefundPolicyVersion)}; calendar {Text(t.CalendarVersion)}; renewal {Text(t.RenewalPolicyVersion)}");
        lines.Add($"Billing time zone: {t.BillingTimeZone}");
        foreach (var shop in t.Shops)
            lines.Add($"Covered shop: {shop.ShopId:D} / {Text(shop.Name)}");
    }

    private static string Instant(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture) + " / " +
        at.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + " Asia/Kolkata";
    private static string Text(string value) => new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
