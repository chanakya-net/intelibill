namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class AddCataloguePublication
{
    private const string ProjectionSecurity = """
        ALTER TABLE public.published_catalogue_offers ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.published_catalogue_offers FORCE ROW LEVEL SECURITY;
        CREATE POLICY catalogue_offer_read ON public.published_catalogue_offers FOR SELECT USING (
            public.catalogue_manager_id() IS NOT NULL OR EXISTS (
                SELECT FROM public.merchant_billing_delegation_uses used
                WHERE used.replay_id = nullif(current_setting('app.catalogue_public_read', true), '')::uuid
                    AND used.account_id IS NULL AND used.action = 'billing.catalogue.read'
                    AND used.actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid));
        CREATE POLICY catalogue_offer_insert ON public.published_catalogue_offers FOR INSERT WITH CHECK (
            EXISTS (
                SELECT FROM public.catalogue_publications publication
                JOIN public.catalogue_preparations prepared USING (admin_id, operation_key)
                JOIN public.plan_versions version ON version.id = publication.version_id
                JOIN public.plans plan ON plan.id = version.plan_id
                WHERE publication.admin_id = public.catalogue_manager_id()
                    AND publication.version_id = published_catalogue_offers.version_id
                    AND version.status = 2 AND plan.kind IN (1, 2)
                    AND version.plan_id = published_catalogue_offers.plan_id
                    AND version.version_number = published_catalogue_offers.version_number
                    AND (version.source = 1) = published_catalogue_offers.development_fixture
                    AND prepared.snapshot::jsonb -> 'plan' = published_catalogue_offers.snapshot::jsonb));
        CREATE TRIGGER published_catalogue_offers_retained BEFORE UPDATE OR DELETE OR TRUNCATE
            ON public.published_catalogue_offers FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();

        CREATE FUNCTION public.require_catalogue_offer() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF OLD.status = 1 AND NEW.status = 2
                AND EXISTS (SELECT FROM public.plans WHERE id = NEW.plan_id AND kind IN (1, 2))
                AND NOT EXISTS (SELECT FROM public.published_catalogue_offers WHERE version_id = NEW.id) THEN
                RAISE EXCEPTION 'A public version requires a catalogue offer' USING ERRCODE = '55000';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER public_catalogue_offer_required AFTER UPDATE ON public.plan_versions
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_catalogue_offer();
        """;
}
