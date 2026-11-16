namespace Intelibill.Licensing.Infrastructure.Migrations;

public partial class AddCataloguePublication
{
    private const string PublicationEvidence = """
        CREATE FUNCTION public.guard_catalogue_version_insert() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
            plan_key uuid := (to_jsonb(NEW) ->> 'plan_id')::uuid;
            valid_source boolean;
        BEGIN
            IF NEW.status IN (2, 3) THEN
                RAISE EXCEPTION 'A new catalogue version begins as a draft' USING ERRCODE = '55000';
            END IF;
            IF NEW.source_version_id IS NULL THEN RETURN NEW; END IF;

            IF plan_key IS NULL THEN
                SELECT EXISTS (SELECT FROM public.trial_policy_versions source_version
                    WHERE source_version.id = NEW.source_version_id AND source_version.status IN (2, 3)
                        AND source_version.revision = NEW.source_expected_version AND source_version.source = NEW.source
                        AND source_version.version_number < NEW.version_number) INTO valid_source;
            ELSE
                SELECT EXISTS (SELECT FROM public.plan_versions source_version
                    WHERE source_version.id = NEW.source_version_id AND source_version.plan_id = plan_key AND source_version.status IN (2, 3)
                        AND source_version.revision = NEW.source_expected_version AND source_version.source = NEW.source
                        AND source_version.version_number < NEW.version_number) INTO valid_source;
            END IF;
            IF NOT valid_source THEN
                RAISE EXCEPTION 'A successor retains its published source' USING ERRCODE = '55000';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER plan_successor_source BEFORE INSERT ON public.plan_versions
            FOR EACH ROW EXECUTE FUNCTION public.guard_catalogue_version_insert();
        CREATE TRIGGER trial_successor_source BEFORE INSERT ON public.trial_policy_versions
            FOR EACH ROW EXECUTE FUNCTION public.guard_catalogue_version_insert();

        CREATE FUNCTION public.require_catalogue_publication_state() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.catalogue_preparations prepared
                WHERE prepared.admin_id = NEW.admin_id AND prepared.operation_key = NEW.operation_key
                    AND prepared.version_id = NEW.version_id
                    AND ((prepared.plan_id IS NULL AND EXISTS (
                        SELECT FROM public.trial_policy_versions version
                        WHERE version.id = NEW.version_id AND version.status = 2
                            AND version.revision = prepared.expected_version + 1))
                      OR (prepared.plan_id IS NOT NULL AND EXISTS (
                        SELECT FROM public.plan_versions version
                        WHERE version.id = NEW.version_id AND version.plan_id = prepared.plan_id AND version.status = 2
                            AND version.revision = prepared.expected_version + 1)))) THEN
                RAISE EXCEPTION 'Catalogue confirmation requires a published version' USING ERRCODE = '55000';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER catalogue_publication_state_required AFTER INSERT ON public.catalogue_publications
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_catalogue_publication_state();
        """;
}
