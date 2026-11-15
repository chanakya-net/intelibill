namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class AddCataloguePublication
{
    private const string PublicationSecurity = """
        ALTER TABLE public.catalogue_preparations ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.catalogue_preparations FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.catalogue_publications ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.catalogue_publications FORCE ROW LEVEL SECURITY;
        CREATE POLICY catalogue_read ON public.catalogue_preparations FOR SELECT
            USING (public.catalogue_manager_id() IS NOT NULL);
        CREATE POLICY catalogue_insert ON public.catalogue_preparations FOR INSERT
            WITH CHECK (admin_id = public.catalogue_manager_id());
        CREATE POLICY catalogue_read ON public.catalogue_publications FOR SELECT
            USING (public.catalogue_manager_id() IS NOT NULL);
        CREATE POLICY catalogue_insert ON public.catalogue_publications FOR INSERT
            WITH CHECK (admin_id = public.catalogue_manager_id() AND EXISTS (
                SELECT FROM public.catalogue_preparations prepared
                WHERE prepared.admin_id = catalogue_publications.admin_id
                    AND prepared.operation_key = catalogue_publications.operation_key
                    AND prepared.version_id = catalogue_publications.version_id
                    AND prepared.prepared_at <= catalogue_publications.published_at));
        CREATE TRIGGER catalogue_preparations_retained BEFORE UPDATE OR DELETE OR TRUNCATE
            ON public.catalogue_preparations FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();
        CREATE TRIGGER catalogue_publications_retained BEFORE UPDATE OR DELETE OR TRUNCATE
            ON public.catalogue_publications FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_catalogue_evidence_loss();

        ALTER POLICY catalogue_revise ON public.plan_versions
            WITH CHECK (updated_by_admin_id = public.catalogue_manager_id());
        ALTER POLICY catalogue_revise ON public.trial_policy_versions
            WITH CHECK (updated_by_admin_id = public.catalogue_manager_id());
        """;

    private const string LifecycleGuard = """
        CREATE OR REPLACE FUNCTION public.guard_catalogue_draft_version() RETURNS trigger
        LANGUAGE plpgsql AS $$
        DECLARE
            old_facts jsonb := to_jsonb(OLD) - ARRAY['status', 'revision', 'updated_at', 'updated_by_admin_id'];
            new_facts jsonb := to_jsonb(NEW) - ARRAY['status', 'revision', 'updated_at', 'updated_by_admin_id'];
            plan_key uuid := (new_facts ->> 'plan_id')::uuid;
            plan_kind integer;
        BEGIN
            IF NEW.revision <> OLD.revision + 1 OR NEW.updated_at < OLD.updated_at OR
                ROW(NEW.id, NEW.version_number, NEW.created_at, NEW.created_by_admin_id, NEW.source, NEW.source_version_id,
                    NEW.source_expected_version, plan_key)
                IS DISTINCT FROM
                ROW(OLD.id, OLD.version_number, OLD.created_at, OLD.created_by_admin_id, OLD.source, OLD.source_version_id,
                    OLD.source_expected_version, (old_facts ->> 'plan_id')::uuid) THEN
                RAISE EXCEPTION 'A catalogue version keeps its identity' USING ERRCODE = '55000';
            END IF;
            IF OLD.status = 1 AND NEW.status = 1 THEN RETURN NEW; END IF;
            IF old_facts IS DISTINCT FROM new_facts OR NOT (
                (OLD.status = 1 AND NEW.status = 2) OR (OLD.status = 2 AND NEW.status = 3)) THEN
                RAISE EXCEPTION 'Published catalogue facts are immutable' USING ERRCODE = '55000';
            END IF;
            IF NEW.status = 3 THEN RETURN NEW; END IF;
            IF NEW.source = 0 OR NOT EXISTS (
                SELECT FROM public.catalogue_publications publication
                JOIN public.catalogue_preparations prepared USING (admin_id, operation_key)
                WHERE publication.version_id = NEW.id AND prepared.version_id = NEW.id
                    AND prepared.plan_id IS NOT DISTINCT FROM plan_key AND prepared.expected_version = OLD.revision
                    AND publication.admin_id = NEW.updated_by_admin_id AND publication.published_at = NEW.updated_at
                    AND (plan_key IS NULL OR EXISTS (
                        SELECT FROM public.trial_policy_versions trial
                        WHERE trial.id = prepared.trial_policy_version_id AND trial.status = 2
                            AND trial.source = NEW.source))) THEN
                RAISE EXCEPTION 'Publication requires matching confirmation evidence' USING ERRCODE = '55000';
            END IF;
            IF plan_key IS NULL THEN
                IF NEW.active_member_cap IS NULL OR NEW.max_concurrent_trial_shops IS NULL OR NEW.trial_terms_version IS NULL THEN
                    RAISE EXCEPTION 'Publication requires complete trial terms' USING ERRCODE = '55000';
                END IF;
            ELSE
                SELECT kind INTO STRICT plan_kind FROM public.plans WHERE id = plan_key;
                IF NEW.included_shops IS NULL OR NEW.members_per_shop IS NULL OR NEW.extra_shops_offered IS NULL
                    OR NEW.customer_terms_version IS NULL OR NEW.refund_policy_version IS NULL
                    OR (plan_kind = 3 AND (NEW.negotiation_reason IS NULL OR
                        (NEW.monthly_price_paise IS NULL AND NEW.annual_price_paise IS NULL)))
                    OR (plan_kind IN (1, 2) AND (NEW.monthly_price_paise IS NULL OR NEW.annual_price_paise IS NULL))
                    OR (plan_kind = 1 AND (NEW.included_shops <> 1 OR NEW.extra_shops_offered))
                    OR (plan_kind = 2 AND NEW.included_shops < 2)
                    OR (plan_kind = 3 AND NEW.extra_shops_offered)
                    OR (NEW.extra_shops_offered AND
                        (NEW.extra_shop_monthly_price_paise IS NULL OR NEW.extra_shop_annual_price_paise IS NULL)) THEN
                    RAISE EXCEPTION 'Publication requires complete plan terms' USING ERRCODE = '55000';
                END IF;
            END IF;
            RETURN NEW;
        END $$;
        """;
}
