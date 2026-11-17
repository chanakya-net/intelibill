namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaymentProviderRouting
{
    private const string ProfileBindings = """
        CREATE TABLE public.payment_provider_bindings (
            pin jsonb PRIMARY KEY, profile_id varchar(80) NOT NULL,
            credential_reference varchar(512), webhook_reference varchar(512),
            registered_at timestamptz NOT NULL CHECK (isfinite(registered_at)),
            provenance text NOT NULL DEFAULT 'PlatformOwner' CHECK (provenance IN ('PlatformOwner', 'DeploymentConfiguration'))
        );
        ALTER TABLE public.payment_provider_bindings ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_provider_bindings FORCE ROW LEVEL SECURITY;
        CREATE POLICY safe_references ON public.payment_provider_bindings FOR SELECT USING (true);
        CREATE POLICY owner_registration ON public.payment_provider_bindings FOR INSERT WITH CHECK (
            provenance = 'PlatformOwner' AND EXISTS (SELECT FROM public.admin_identities a JOIN public.admin_sessions s ON s.admin_id = a.id
                WHERE a.id = nullif(current_setting('app.provider_admin_id', true), '')::uuid
                    AND s.id = nullif(current_setting('app.provider_session_id', true), '')::uuid
                    AND a.deactivated_at IS NULL AND a.mfa_enrolled_at IS NOT NULL AND (a.capabilities & 16) = 16
                    AND s.revoked_at IS NULL AND registered_at >= s.mfa_verified_at
                    AND registered_at < s.mfa_verified_at + interval '5 minutes'));
        CREATE POLICY deployment_registration ON public.payment_provider_bindings FOR INSERT WITH CHECK (
            provenance = 'DeploymentConfiguration' AND EXISTS (SELECT FROM public.payment_provider_routing WHERE id = 1 AND revision = 0)
            AND EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
                    AND u.account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
                    AND u.xmin = pg_current_xact_id()::xid AND u.action = 'billing.checkout.payment.start'));
        CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.payment_provider_bindings
            FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_provider_bindings
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;
}
