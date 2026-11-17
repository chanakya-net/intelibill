namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaymentProviderRouting
{
    private const string SimulatorIsolation = """
        CREATE POLICY pinned_identity ON public.development_gateway_sessions AS RESTRICTIVE FOR ALL
            USING (provider_account_id = current_setting('app.gateway_account', true)
                AND environment = current_setting('app.gateway_environment', true)
                AND configuration_version = current_setting('app.gateway_configuration', true))
            WITH CHECK (provider_account_id = current_setting('app.gateway_account', true)
                AND environment = current_setting('app.gateway_environment', true)
                AND configuration_version = current_setting('app.gateway_configuration', true));
        CREATE POLICY pinned_identity ON public.development_gateway_payments AS RESTRICTIVE FOR ALL
            USING (EXISTS (SELECT FROM public.development_gateway_sessions s
                WHERE s.provider_order_reference = development_gateway_payments.provider_order_reference))
            WITH CHECK (EXISTS (SELECT FROM public.development_gateway_sessions s
                WHERE s.provider_order_reference = development_gateway_payments.provider_order_reference));
        CREATE POLICY pinned_identity ON public.development_gateway_controls AS RESTRICTIVE FOR ALL
            USING (EXISTS (SELECT FROM public.development_gateway_sessions s
                WHERE s.provider_order_reference = development_gateway_controls.provider_order_reference))
            WITH CHECK (EXISTS (SELECT FROM public.development_gateway_sessions s
                WHERE s.provider_order_reference = development_gateway_controls.provider_order_reference));
        """;
}
