namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0303Tests
{
    internal static Task CaptureOriginalSessionAsync(P0302Fixture fixture, Guid attempt) =>
        LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO public.development_gateway_payments
                (provider_payment_reference, provider_order_reference, amount_paise, currency, state, captured_at)
            SELECT 'recovery_fixture', provider_order_reference, amount_paise, currency, 'Captured', created_at
            FROM public.development_gateway_sessions WHERE attempt_id = '{attempt:D}'
            """);
}
