namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class AddCatalogueRetirement
{
    private const string RetirementSecurity = """
        ALTER TABLE public.catalogue_retirements ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.catalogue_retirements FORCE ROW LEVEL SECURITY;
        CREATE POLICY catalogue_retirement_read ON public.catalogue_retirements FOR SELECT
            USING (public.catalogue_manager_id() IS NOT NULL);
        CREATE POLICY catalogue_retirement_insert ON public.catalogue_retirements FOR INSERT
            WITH CHECK (admin_id = public.catalogue_manager_id());
        CREATE TRIGGER catalogue_retirements_retained BEFORE UPDATE OR DELETE OR TRUNCATE
            ON public.catalogue_retirements FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();

        ALTER TABLE public.withdrawn_catalogue_offers ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.withdrawn_catalogue_offers FORCE ROW LEVEL SECURITY;
        CREATE POLICY catalogue_withdrawal_read ON public.withdrawn_catalogue_offers FOR SELECT USING (
            EXISTS (SELECT FROM public.published_catalogue_offers offer WHERE offer.version_id = withdrawn_catalogue_offers.version_id));
        CREATE POLICY catalogue_withdrawal_insert ON public.withdrawn_catalogue_offers FOR INSERT WITH CHECK (
            EXISTS (SELECT FROM public.catalogue_retirements retirement
                WHERE retirement.version_id = withdrawn_catalogue_offers.version_id
                    AND retirement.admin_id = public.catalogue_manager_id()));
        CREATE TRIGGER withdrawn_catalogue_offers_retained BEFORE UPDATE OR DELETE OR TRUNCATE
            ON public.withdrawn_catalogue_offers FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();
        """;

    private const string RetirementEvidence = """
        CREATE FUNCTION public.require_plan_retirement_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.catalogue_retirements retirement WHERE retirement.version_id = NEW.id)
                AND NOT EXISTS (SELECT FROM public.plan_versions successor
                    WHERE successor.plan_id = NEW.plan_id AND successor.version_number > NEW.version_number AND successor.status = 2) THEN
                RAISE EXCEPTION 'A plan retirement needs evidence or a published successor' USING ERRCODE = '55000';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER plan_retirement_evidence_required AFTER UPDATE ON public.plan_versions
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (OLD.status = 2 AND NEW.status = 3)
            EXECUTE FUNCTION public.require_plan_retirement_evidence();

        CREATE FUNCTION public.require_catalogue_retirement_state() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.plan_versions version JOIN public.catalogue_publications publication ON publication.version_id = version.id
                WHERE version.id = NEW.version_id AND version.plan_id = NEW.plan_id AND version.status = 3
                    AND version.revision - 1 = NEW.expected_version
                    AND version.updated_by_admin_id = NEW.admin_id AND version.updated_at = NEW.retired_at
                    AND publication.published_at <= NEW.retired_at)
                OR (EXISTS (SELECT FROM public.published_catalogue_offers WHERE version_id = NEW.version_id)
                    AND NOT EXISTS (SELECT FROM public.withdrawn_catalogue_offers WHERE version_id = NEW.version_id)) THEN
                RAISE EXCEPTION 'A retirement needs matching version state and public withdrawal' USING ERRCODE = '55000';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER catalogue_retirement_state_required AFTER INSERT ON public.catalogue_retirements
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_catalogue_retirement_state();
        """;

    private const string RemoveRetirementGuards = """
        LOCK TABLE public.plan_versions, public.catalogue_retirements, public.withdrawn_catalogue_offers IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.catalogue_retirements NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.catalogue_retirements) THEN
                RAISE EXCEPTION 'Catalogue retirement evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TRIGGER plan_retirement_evidence_required ON public.plan_versions;
        DROP FUNCTION public.require_plan_retirement_evidence();
        DROP TRIGGER catalogue_retirement_state_required ON public.catalogue_retirements;
        DROP FUNCTION public.require_catalogue_retirement_state();
        """;
}
