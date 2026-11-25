using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// Account-scoped refund request evidence (P06-08). A request records the customer's stated amount and reason against
/// a verified payment confirmation without approving the refund, moving money or changing term/shop access.
/// </summary>
public static class RefundRequestsV1
{
    public const string Policy = "refund-request/1";
    public const string Create = "billing.refund-request.create";
    public const string List = "billing.refund-request.list";
    public const string Requested = "Requested";
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Failed = "Failed";
    public const string Unknown = "Unknown";
    public const int MaxReasonLength = 500;
    // Fits a 500-char reason with worst-case JSON escaping (\uXXXX = 6 bytes/unit) plus UUIDs and fields.
    public const int MaxCreateBytes = 4096;
    public const int MaxRequestBytes = 4096;
    public const int PageSize = 25;
    public const int MaxPageBytes = PageSize * (MaxReasonLength * 6 + 512) + 512;
    public const string Invalid = "Billing.RefundRequestInvalid";
    public const string AmountInvalid = "Billing.RefundAmountInvalid";
    public const string PaymentNotFound = "Billing.RefundPaymentNotFound";

    public static readonly string[] States = [Requested, Pending, Confirmed, Failed, Unknown];

    public static bool IsAction(string action) => action is Create or List;

    public static string Path(Guid account) =>
        $"{MerchantBillingDelegationV1.AccountsPath}/{account:D}/refund-requests";

    public static string ListPath(Guid account, Guid? after) => after is { } cursor
        ? $"{Path(account)}/after/{cursor:D}" : Path(account);

    public static string? NormalizeReason(string? value) =>
        value?.Trim() is { Length: > 0 and <= MaxReasonLength } reason && !reason.Any(char.IsControl) ? reason : null;

    // Canonical ordered JSON array: lower-case D UUIDs, integer paise and normalized reason text.
    public static string CreateHash(Guid orderId, Guid confirmationId, long amountPaise, string normalizedReason) =>
        CheckoutOrdersV1.Digest(Policy, "create", orderId, confirmationId, amountPaise, normalizedReason);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateRefundRequestV1(
    [property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("confirmationId")] Guid ConfirmationId,
    [property: JsonPropertyName("amountPaise")] long AmountPaise,
    [property: JsonPropertyName("reason")] string? Reason)
{
    public bool HasValidIdentity() => OperationKey != Guid.Empty && OrderId != Guid.Empty && ConfirmationId != Guid.Empty;
    public bool HasValidAmount() => AmountPaise is > 0 and <= CheckoutOrdersV1.MaxPaise;
    public override string ToString() => "[REDACTED]";
}

/// <summary>Customer-visible refund request fact. Provider-neutral: carries no gateway identifiers or references.</summary>
public sealed record RefundRequestV1(
    Guid RefundRequestId,
    Guid AccountId,
    Guid OrderId,
    Guid ConfirmationId,
    string Status,
    long AmountPaise,
    long PaymentAmountPaise,
    string Currency,
    string Reason,
    DateTimeOffset RequestedAt)
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ApprovedAmountPaise { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderState { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessDisposition { get; init; }

    public override string ToString() => "[REDACTED]";
}

public sealed record RefundRequestPageV1(Guid AccountId, RefundRequestV1[] Items, Guid? NextCursor)
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
}
