namespace Intelibill.Infrastructure.Billing;

/// <summary>
/// How often the merchant consent and trial delivery workers look for pending deliveries.
/// </summary>
/// <remarks>
/// Production keeps the three-second default. Integration hosts that drive a consent end to end
/// lower it so a test does not idle until the next tick.
/// </remarks>
internal sealed class MerchantDeliveryPollingOptions
{
    internal const string Section = "MerchantDeliveryPolling";

    internal static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(10);
    internal static readonly TimeSpan Maximum = TimeSpan.FromMinutes(5);

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(3);

    internal bool IsValid() => Interval >= Minimum && Interval <= Maximum;
}
