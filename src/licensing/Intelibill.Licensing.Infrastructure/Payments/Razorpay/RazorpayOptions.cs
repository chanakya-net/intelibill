using Intelibill.Licensing.Application.Payments;

namespace Intelibill.Licensing.Infrastructure.Payments.Razorpay;

/// <summary>Bound from <c>Payments:Razorpay</c>. Credentials come only from configuration or a secret store.</summary>
internal sealed class RazorpayOptions
{
    public const string Section = "Payments:Razorpay";
    public string? KeyId { get; init; }
    public string? KeySecret { get; init; }
    public string? AccountId { get; init; }
    public string? Environment { get; init; }
    public string? ConfigurationVersion { get; init; }
    public string BaseUrl { get; init; } = "https://api.razorpay.com/";
    public string Currency { get; init; } = "INR";

    /// <summary>Checkout methods the account offers. Instalment and deferred methods are never full payment.</summary>
    public string[] Methods { get; init; } = ["card", "netbanking", "wallet", "upi"];

    /// <summary>The account's per-order maximum in paise. Required: Razorpay's limit is account-level and never assumed.</summary>
    public long? MaxAmountPaise { get; init; }

    /// <summary>Must stay false: an order accepting a partial amount cannot fund a full-price purchase.</summary>
    public bool AllowPartialPayment { get; init; }
}

/// <summary>A validated, pinned Razorpay configuration. Absent when any capability or credential is unsupported.</summary>
internal sealed record RazorpayConfiguration(string KeyId, string KeySecret, Uri BaseAddress, PaymentProviderPin Pin,
    IReadOnlySet<string> Methods, long MaxAmountPaise)
{
    internal const string ProviderId = "razorpay";
    /// <summary>Razorpay's documented minimum order, 1 INR (100 paise).</summary>
    internal const long MinAmountPaise = 100;
    /// <summary>The only methods a full-price checkout may use; instalment and deferred methods are never listed.</summary>
    internal static readonly string[] Supported = ["card", "netbanking", "wallet", "upi"];

    /// <summary>The capability validator: pins account, environment and version, and refuses unsupported behaviour.</summary>
    internal static RazorpayConfiguration? From(RazorpayOptions options)
    {
        var environment = options.Environment;
        var prefix = environment switch { "Test" => "rzp_test_", "Live" => "rzp_live_", _ => null };
        if (prefix is null || options.KeyId is not { Length: > 9 } keyId || !keyId.StartsWith(prefix, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(options.KeySecret) || string.IsNullOrWhiteSpace(options.AccountId) ||
            string.IsNullOrWhiteSpace(options.ConfigurationVersion) || options.AllowPartialPayment ||
            options.Currency != "INR" || options.Methods.Length == 0 ||
            options.Methods.Any(method => !Supported.Contains(method, StringComparer.Ordinal)) ||
            options.MaxAmountPaise is not { } max || max < MinAmountPaise ||
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseAddress) ||
            baseAddress != new Uri("https://api.razorpay.com/"))
            return null;
        return new(keyId, options.KeySecret, baseAddress,
            new(ProviderId, options.AccountId, environment!, options.ConfigurationVersion),
            options.Methods.ToHashSet(StringComparer.Ordinal), options.MaxAmountPaise.Value);
    }
}
