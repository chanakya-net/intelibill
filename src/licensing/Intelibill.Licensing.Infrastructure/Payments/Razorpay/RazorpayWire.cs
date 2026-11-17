using System.Globalization;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Infrastructure.Payments.Razorpay;

/// <summary>The provider's order entity, reduced to the fields licensing relies on.</summary>
internal sealed record WireOrder(string Id, long Amount, string Currency, string? Receipt, string? Status,
    string? AccountNote, string? OrderNote, string? AttemptNote, bool PartialPayment);

/// <summary>The provider's payment entity, reduced to the fields licensing relies on.</summary>
internal sealed record WirePayment(string Id, string OrderId, long Amount, string Currency, string Status, bool Captured,
    DateTimeOffset CreatedAt);

/// <summary>Strict parsers for documented Razorpay entities; anything unexpected is a rejection, never a guess.</summary>
internal static class RazorpayWire
{
    internal static WireOrder? Order(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !Text(item, "entity", out var entity) || entity != "order" ||
            !Text(item, "id", out var id) || !id.StartsWith("order_", StringComparison.Ordinal) || id.Length > 120 ||
            !Integer(item, "amount", out var amount) || !Text(item, "currency", out var currency))
            return null;
        Text(item, "receipt", out var receipt);
        Text(item, "status", out var status);
        string? account = null, order = null, attempt = null;
        if (item.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Object)
        {
            Text(notes, "account_id", out account);
            Text(notes, "order_id", out order);
            Text(notes, "attempt_id", out attempt);
        }
        var partial = item.TryGetProperty("partial_payment", out var flag) && flag.ValueKind == JsonValueKind.True;
        return new(id, amount, currency, receipt, status, account, order, attempt, partial);
    }

    internal static WirePayment? Payment(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !Text(item, "entity", out var entity) || entity != "payment" ||
            !Text(item, "id", out var id) || !id.StartsWith("pay_", StringComparison.Ordinal) || id.Length > 120 ||
            !Text(item, "order_id", out var order) || !Integer(item, "amount", out var amount) ||
            !Text(item, "currency", out var currency) || !Text(item, "status", out var status) ||
            !Integer(item, "created_at", out var created) || created <= 0 || created > 253_402_300_799 ||
            !item.TryGetProperty("captured", out var captured) ||
            captured.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return null;
        return new(id, order, amount, currency, status, captured.ValueKind == JsonValueKind.True,
            DateTimeOffset.FromUnixTimeSeconds(created));
    }

    /// <summary>Items of a documented <c>collection</c> response, or null when the shape is wrong.</summary>
    internal static IReadOnlyList<JsonElement>? Items(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !Text(root, "entity", out var entity) || entity != "collection" ||
            !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 100)
            return null;
        return [.. items.EnumerateArray()];
    }

    internal static PaymentObservation Observe(PaymentProviderPin pin, WirePayment payment) => new(pin, payment.OrderId, payment.Id,
        payment.Captured && payment.Status == "captured" ? ObservedPaymentState.Captured
            : payment.Status == "authorized" ? ObservedPaymentState.Authorized
            : payment.Status == "failed" ? ObservedPaymentState.Failed : ObservedPaymentState.Pending,
        payment.Amount, payment.Currency,
        payment.Captured && payment.Status == "captured" ? payment.CreatedAt : null);

    private static bool Text(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String ||
            property.GetString() is not { Length: > 0 and <= 200 } text)
            return false;
        value = text;
        return true;
    }

    private static bool Integer(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out value) && value >= 0;
    }

    internal static string Receipt(Guid attempt) => attempt.ToString("N", CultureInfo.InvariantCulture);
}
