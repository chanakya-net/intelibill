namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class AddCataloguePublication
{
    private const string RequireEmptyPublication = """
        LOCK TABLE public.plan_versions, public.trial_policy_versions, public.catalogue_preparations,
            public.catalogue_publications, public.published_catalogue_offers IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.plan_versions NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.trial_policy_versions NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.catalogue_preparations NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.catalogue_publications NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.published_catalogue_offers NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.catalogue_preparations)
                OR EXISTS (SELECT FROM public.catalogue_publications)
                OR EXISTS (SELECT FROM public.published_catalogue_offers)
                OR EXISTS (SELECT FROM public.plan_versions WHERE source <> 0 OR source_version_id IS NOT NULL)
                OR EXISTS (SELECT FROM public.trial_policy_versions WHERE source <> 0 OR source_version_id IS NOT NULL) THEN
                RAISE EXCEPTION 'Catalogue publication or provenance evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.plan_versions FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.trial_policy_versions FORCE ROW LEVEL SECURITY;
        """;

    private const string RestoreDraftGuard = """
        DROP TRIGGER catalogue_publication_state_required ON public.catalogue_publications;
        DROP FUNCTION public.require_catalogue_publication_state();
        DROP TRIGGER plan_successor_source ON public.plan_versions;
        DROP TRIGGER trial_successor_source ON public.trial_policy_versions;
        DROP FUNCTION public.guard_catalogue_version_insert();
        DROP TRIGGER public_catalogue_offer_required ON public.plan_versions;
        DROP FUNCTION public.require_catalogue_offer();
        ALTER POLICY catalogue_revise ON public.plan_versions
            WITH CHECK (updated_by_admin_id = public.catalogue_manager_id() AND status = 1);
        ALTER POLICY catalogue_revise ON public.trial_policy_versions
            WITH CHECK (updated_by_admin_id = public.catalogue_manager_id() AND status = 1);

        CREATE OR REPLACE FUNCTION public.guard_catalogue_draft_version() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            IF OLD.status <> 1 OR NEW.status <> 1 OR NEW.revision <> OLD.revision + 1
                OR NEW.id <> OLD.id OR NEW.version_number <> OLD.version_number
                OR NEW.created_at <> OLD.created_at OR NEW.created_by_admin_id <> OLD.created_by_admin_id THEN
                RAISE EXCEPTION 'Only an open draft advances, one revision at a time' USING ERRCODE = '55000';
            END IF;
            IF TG_TABLE_NAME = 'plan_versions' THEN
                IF NEW.plan_id <> OLD.plan_id THEN
                    RAISE EXCEPTION 'A plan version keeps its plan' USING ERRCODE = '55000';
                END IF;
            END IF;
            RETURN NEW;
        END $$;
        """;
}
