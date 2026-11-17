namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaymentProviderRouting
{
    private const string RoutingEvents = """
        CREATE TABLE public.payment_routing_events (
            sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            account_id uuid NOT NULL, attempt_id uuid NOT NULL,
            kind text NOT NULL CHECK (kind IN ('Unavailable', 'Restored')),
            reason text CHECK (reason IN ('MissingProfile', 'InvalidProfile', 'UnknownProvider', 'CredentialUnavailable', 'SimulatorForbidden', 'ConfigurationReused')),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            CHECK ((kind = 'Restored') = (reason IS NULL)),
            FOREIGN KEY (account_id, attempt_id) REFERENCES public.payment_attempts(account_id, attempt_id)
        );
        CREATE INDEX routing_event_attempt ON public.payment_routing_events(attempt_id, sequence DESC);
        ALTER TABLE public.payment_routing_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_routing_events FORCE ROW LEVEL SECURITY;
        CREATE POLICY account_read ON public.payment_routing_events FOR SELECT
            USING (account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid);
        CREATE POLICY attempt_append ON public.payment_routing_events FOR INSERT WITH CHECK (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
            AND attempt_id = nullif(current_setting('app.payment_attempt_id', true), '')::uuid);
        CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.payment_routing_events
            FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_routing_events
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;
}
