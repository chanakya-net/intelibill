namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class PaymentProviderRouting
{
    private const string SelectionTables = """
        CREATE TABLE public.payment_provider_routing (
            id integer PRIMARY KEY CHECK (id = 1), revision bigint NOT NULL CHECK (revision >= 0),
            profile_id varchar(80), pin jsonb,
            CHECK ((revision = 0 AND profile_id IS NULL AND pin IS NULL) OR (revision > 0 AND profile_id IS NOT NULL AND pin IS NOT NULL))
        );
        INSERT INTO public.payment_provider_routing VALUES (1, 0, NULL, NULL);
        CREATE TABLE public.payment_provider_selections (
            revision bigint PRIMARY KEY CHECK (revision > 0), profile_id varchar(80) NOT NULL,
            pin jsonb NOT NULL, previous_pin jsonb,
            provenance text NOT NULL CHECK (provenance IN ('PlatformOwner', 'DeploymentConfiguration')),
            admin_id uuid, session_id uuid, mfa_verified_at timestamptz, operation_key uuid UNIQUE,
            request_hash text, reason varchar(255) NOT NULL CHECK (length(trim(reason)) BETWEEN 3 AND 255),
            selected_at timestamptz NOT NULL CHECK (isfinite(selected_at)), receipt jsonb,
            CHECK ((provenance = 'DeploymentConfiguration' AND revision = 1 AND admin_id IS NULL AND session_id IS NULL
                    AND operation_key IS NULL AND request_hash IS NULL AND receipt IS NULL) OR
                (provenance = 'PlatformOwner' AND admin_id IS NOT NULL AND session_id IS NOT NULL AND mfa_verified_at IS NOT NULL
                    AND operation_key IS NOT NULL AND request_hash ~ '^[a-f0-9]{64}$' AND receipt IS NOT NULL
                    AND selected_at >= mfa_verified_at AND selected_at < mfa_verified_at + interval '5 minutes'))
        );
        ALTER TABLE public.payment_attempts ADD COLUMN selection_revision bigint
            REFERENCES public.payment_provider_selections(revision);
        ALTER TABLE public.payment_provider_routing ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_provider_routing FORCE ROW LEVEL SECURITY;
        CREATE POLICY safe_read ON public.payment_provider_routing FOR SELECT USING (true);
        CREATE POLICY select_provider ON public.payment_provider_routing FOR UPDATE USING (true) WITH CHECK (true);
        ALTER TABLE public.payment_provider_selections ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.payment_provider_selections FORCE ROW LEVEL SECURITY;
        CREATE POLICY safe_read ON public.payment_provider_selections FOR SELECT USING (true);
        CREATE POLICY append_selection ON public.payment_provider_selections FOR INSERT WITH CHECK (
            (provenance = 'DeploymentConfiguration' AND nullif(current_setting('app.billing_actor_id', true), '') IS NOT NULL)
            OR (provenance = 'PlatformOwner' AND admin_id = nullif(current_setting('app.provider_admin_id', true), '')::uuid
                AND session_id = nullif(current_setting('app.provider_session_id', true), '')::uuid
                AND EXISTS (SELECT FROM public.admin_identities a JOIN public.admin_sessions s ON s.admin_id = a.id
                    WHERE a.id = payment_provider_selections.admin_id AND s.id = payment_provider_selections.session_id AND a.deactivated_at IS NULL AND a.mfa_enrolled_at IS NOT NULL
                        AND (a.capabilities & 16) = 16 AND s.revoked_at IS NULL AND s.mfa_verified_at = payment_provider_selections.mfa_verified_at)));
        CREATE TRIGGER retain_rows BEFORE UPDATE OR DELETE ON public.payment_provider_selections
            FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_provider_selections
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_row BEFORE DELETE ON public.payment_provider_routing
            FOR EACH ROW EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_table BEFORE TRUNCATE ON public.payment_provider_routing
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE FUNCTION public.guard_payment_provider_selection() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NEW.id <> OLD.id OR NEW.revision <> OLD.revision + 1 OR NOT EXISTS (
                SELECT FROM public.payment_provider_selections e WHERE e.revision = NEW.revision
                    AND e.profile_id = NEW.profile_id AND e.pin = NEW.pin
                    AND coalesce(e.previous_pin, 'null'::jsonb) = coalesce(OLD.pin, 'null'::jsonb)) THEN
                RAISE EXCEPTION 'An immutable selection audit is required' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_selection BEFORE UPDATE ON public.payment_provider_routing
            FOR EACH ROW EXECUTE FUNCTION public.guard_payment_provider_selection();
        """;
}
