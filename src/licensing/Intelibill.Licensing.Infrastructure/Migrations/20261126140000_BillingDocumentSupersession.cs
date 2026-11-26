using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// P06-13: Enables append-only superseding accounting invoice versions with a finance-only reason,
/// strict predecessor lineage verification and fork prevention under concurrency.
/// </summary>
[DbContext(typeof(LicensingDbContext))]
[Migration("20261126140000_BillingDocumentSupersession")]
public sealed class BillingDocumentSupersession : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public.billing_document_versions
            ADD COLUMN supersession_reason text,
            DROP CONSTRAINT billing_document_initial_only,
            ADD CONSTRAINT billing_document_supersession_reason CHECK (
                (version_number = 1 AND supersession_reason IS NULL)
                OR (version_number > 1 AND supersession_reason IS NOT NULL
                    AND length(supersession_reason) BETWEEN 1 AND 400
                    AND supersession_reason = btrim(supersession_reason)
                    AND supersession_reason !~ '[[:cntrl:]]'));

        CREATE UNIQUE INDEX billing_document_supersedes_unique
            ON public.billing_document_versions (account_id, supersedes_version_id)
            WHERE supersedes_version_id IS NOT NULL;

        CREATE INDEX billing_document_order_version
            ON public.billing_document_versions (account_id, order_id, version_number DESC, version_id);

        CREATE FUNCTION public.guard_billing_document_supersession() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
            parent public.billing_document_versions%ROWTYPE;
        BEGIN
            IF NEW.version_number = 1 THEN
                RETURN NULL;
            END IF;
            SELECT * INTO parent
            FROM public.billing_document_versions p
            WHERE p.account_id = NEW.account_id AND p.version_id = NEW.supersedes_version_id;
            IF NOT FOUND THEN
                RETURN NULL;
            END IF;
            IF parent.order_id <> NEW.order_id
                OR parent.document_id <> NEW.document_id
                OR parent.kind <> NEW.kind
                OR parent.version_number <> NEW.version_number - 1 THEN
                RAISE EXCEPTION 'Superseding billing document predecessor mismatch' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END;
        $$;

        CREATE TRIGGER check_billing_document_supersession
            AFTER INSERT ON public.billing_document_versions
            FOR EACH ROW EXECUTE FUNCTION public.guard_billing_document_supersession();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.billing_document_versions IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.billing_document_versions NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.billing_document_versions WHERE version_number > 1 OR supersession_reason IS NOT NULL) THEN
                RAISE EXCEPTION 'Superseding accounting invoice evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        ALTER TABLE public.billing_document_versions FORCE ROW LEVEL SECURITY;

        DROP TRIGGER check_billing_document_supersession ON public.billing_document_versions;
        DROP FUNCTION public.guard_billing_document_supersession();
        DROP INDEX public.billing_document_order_version;
        DROP INDEX public.billing_document_supersedes_unique;
        ALTER TABLE public.billing_document_versions
            DROP CONSTRAINT billing_document_supersession_reason,
            DROP COLUMN supersession_reason,
            ADD CONSTRAINT billing_document_initial_only CHECK (version_number = 1);
        """);
}
