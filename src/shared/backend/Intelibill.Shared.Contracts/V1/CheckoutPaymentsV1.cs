using System.Text.Json.Serialization;

namespace Intelibill.Shared.Contracts.V1;

/// <summary>
/// Provider-neutral checkout payment contract. No provider identifier, status or reference is part of it:
/// those stay inside licensing gateway adapters. Launch parameters are an allow-listed, provider-free map.
/// </summary>
public static class CheckoutPaymentsV1
{
    public static bool HasValidPaymentSource(CheckoutPaymentV1 p)
    {
        if (p.ManualPayment is not { } manual)
            return (p.Payment == "NotStarted") == (p.AttemptId is null);

        if (manual.Payment != "Verified" || manual.VerifiedAt.Offset != TimeSpan.Zero ||
            manual.VerifiedAt <= DateTimeOffset.UnixEpoch || p.CanStart || p.Launch is not null ||
            p.Fulfilment == "NotFulfilled")
            return false;

        return p.AttemptId is null
            ? p.Payment == "Verified" && p.StartedAt is null && p.VerifiedAt == manual.VerifiedAt
            : p.Payment != "NotStarted" && p.StartedAt is not null && (p.Payment == "Verified") == (p.VerifiedAt is not null);
    }

    public static bool HasValidRestoration(CheckoutPaymentV1 payment) =>
        (!payment.RestorationHeld || payment.Fulfilment == "NeedsReview" &&
            (payment.Payment == "Verified" || payment.ManualPayment is not null)) &&
        (payment.ServiceRestoration is null || payment.ServiceRestoration is { Count: > 0 } adjustment &&
            adjustment.GrantId != Guid.Empty && EntitlementSnapshotsV1.IsInstant(adjustment.StartsAt) &&
            EntitlementSnapshotsV1.IsInstant(adjustment.EndsAt) && adjustment.EndsAt > adjustment.StartsAt &&
            payment.Fulfilment == "Fulfilled");

    public const string Policy = "checkout-payment/1";
    public const string Start = "billing.checkout.payment.start";
    public const string Read = "billing.checkout.payment.read";
    public const string Verify = "billing.checkout.payment.verify";
    public const string Simulate = "billing.checkout.payment.simulate";
    public const int MaxStartBytes = 512;
    public const int MaxVerifyBytes = 256;
    public const int MaxStatusBytes = 2048;
    public const string SimulatorFlow = "development-simulator";

    public const string Unavailable = "Billing.PaymentUnavailable";
    public const string Unsupported = "Billing.PaymentUnsupported";
    public const string NotAccepted = "Billing.PaymentNotAccepted";
    public const string AlreadyStarted = "Billing.PaymentAlreadyStarted";
    public const string NotFound = "Billing.PaymentNotFound";

    public static readonly string[] PaymentStates = ["NotStarted", "AwaitingPayment", "PendingVerification", "Verified", "Failed"];
    public static readonly string[] FulfilmentStates = ["NotFulfilled", "Fulfilled", "NeedsReview"];
    public static readonly string[] AccessStates = ["NotApplicable", "Applying", "Applied", "NeedsReview"];
    public static readonly string[] LauncherFlows = [SimulatorFlow];
    public static readonly string[] LauncherOutcomes = ["Success", "Cancelled"];

    public static bool IsAction(string action) => action is Start or Read or Verify or Simulate;
    public static string PaymentPath(Guid account, Guid order) => $"{CheckoutOrdersV1.DetailPath(account, order)}/payment";
    public static string AttemptsPath(Guid account, Guid order) => $"{PaymentPath(account, order)}/attempts";
    public static string VerifyPath(Guid account, Guid order, Guid attempt) => $"{AttemptsPath(account, order)}/{attempt:D}/verify";
    public static string CapturePath(Guid account, Guid order, Guid attempt) =>
        $"{AttemptsPath(account, order)}/{attempt:D}/simulator/capture";

    public static string StartHash(Guid order, StartCheckoutPaymentV1 value) =>
        CheckoutOrdersV1.Digest(Policy, "start", order, value.ExpectedRevision, value.SnapshotHash);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartCheckoutPaymentV1([property: JsonPropertyName("operationKey")] Guid OperationKey,
    [property: JsonPropertyName("expectedRevision")] int ExpectedRevision,
    [property: JsonPropertyName("snapshotHash")] string SnapshotHash)
{
    public bool IsValid() => OperationKey != Guid.Empty && ExpectedRevision > 0 && CheckoutOrdersV1.IsHash(SnapshotHash);
}

/// <summary>The browser launcher's own report. A hint recorded as a claim; it never proves payment.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VerifyCheckoutPaymentV1([property: JsonPropertyName("launcherOutcome")] string? LauncherOutcome = null)
{
    public bool IsValid() => LauncherOutcome is null || CheckoutPaymentsV1.LauncherOutcomes.Contains(LauncherOutcome);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SimulateCheckoutCaptureV1([property: JsonPropertyName("outcome")] string Outcome)
{
    public bool IsValid() => Outcome is "Captured" or "Timeout" or "Delayed" or "Failed";
}

public sealed record CheckoutLaunchV1(
    [property: JsonPropertyName("flow")] string Flow,
    [property: JsonPropertyName("parameters")] IReadOnlyDictionary<string, string> Parameters);

public sealed record ManualCheckoutPaymentV1(
    [property: JsonPropertyName("payment")] string Payment,
    [property: JsonPropertyName("verifiedAt")] DateTimeOffset VerifiedAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ServiceRestorationAdjustmentV1(int Count, Guid GrantId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public sealed record CheckoutPaymentV1(
    [property: JsonPropertyName("accountId")] Guid AccountId,
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("attemptId")] Guid? AttemptId,
    [property: JsonPropertyName("payment")] string Payment,
    [property: JsonPropertyName("fulfilment")] string Fulfilment,
    [property: JsonPropertyName("access")] string Access,
    [property: JsonPropertyName("amountPaise")] long AmountPaise,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("startedAt")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("verifiedAt")] DateTimeOffset? VerifiedAt,
    [property: JsonPropertyName("termId")] Guid? TermId,
    [property: JsonPropertyName("termStartsAt")] DateTimeOffset? TermStartsAt,
    [property: JsonPropertyName("termEndsAt")] DateTimeOffset? TermEndsAt,
    [property: JsonPropertyName("shopsTotal")] int ShopsTotal,
    [property: JsonPropertyName("shopsApplied")] int ShopsApplied,
    [property: JsonPropertyName("canStart")] bool CanStart,
    [property: JsonPropertyName("launch")] CheckoutLaunchV1? Launch,
    [property: JsonPropertyName("manualPayment"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ManualCheckoutPaymentV1? ManualPayment = null,
    [property: JsonPropertyName("restorationHeld"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool RestorationHeld = false,
    [property: JsonPropertyName("serviceRestoration"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ServiceRestorationAdjustmentV1? ServiceRestoration = null)
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;
}
