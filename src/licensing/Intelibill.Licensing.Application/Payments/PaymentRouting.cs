namespace Intelibill.Licensing.Application.Payments;

internal static class PaymentRouting
{
    internal static string Blocker(IPaymentGatewayResolver gateways, PaymentProviderPin pin) =>
        (gateways as IPaymentProviderRegistry)?.Profiles().FirstOrDefault(p => p.Pin == pin)?.Blocker ?? "MissingProfile";
}
