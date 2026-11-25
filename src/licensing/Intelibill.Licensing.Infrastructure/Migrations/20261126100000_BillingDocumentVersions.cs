using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261126100000_BillingDocumentVersions")]
public sealed class BillingDocumentVersions : Migration
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Admin = "nullif(current_setting('app.manual_admin_id', true), '')::uuid";
    private const string Session = "nullif(current_setting('app.manual_session_id', true), '')::uuid";
    private const string Order = "nullif(current_setting('app.manual_order_id', true), '')::uuid";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql($"""
            ALTER TABLE public.billing_document_versions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_document_versions FORCE ROW LEVEL SECURITY;
            CREATE POLICY finance_read ON public.billing_document_versions FOR SELECT USING (
                account_id = {Account} AND {LiveBilling("")});
            CREATE POLICY finance_insert ON public.billing_document_versions FOR INSERT WITH CHECK (
                account_id = {Account} AND order_id = {Order} AND admin_id = {Admin} AND session_id = {Session}
                AND {LiveBilling("AND s.mfa_verified_at = billing_document_versions.mfa_verified_at")});
            CREATE TRIGGER retain_billing_documents BEFORE UPDATE OR DELETE OR TRUNCATE ON public.billing_document_versions
                FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
            """);
    }

    private static string LiveBilling(string evidence) => $"""
        EXISTS (SELECT FROM public.admin_sessions s JOIN public.admin_identities a ON a.id = s.admin_id
            WHERE s.id = {Session} AND a.id = {Admin} AND s.revoked_at IS NULL
                AND a.mfa_enrolled_at IS NOT NULL AND a.deactivated_at IS NULL AND (a.capabilities & 2) = 2 {evidence})
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.billing_document_versions IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.billing_document_versions NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.billing_document_versions) THEN
                RAISE EXCEPTION 'Accounting invoice evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TABLE public.billing_document_versions;
        """);

    private const string Tables = """
        CREATE TABLE public.billing_document_versions (
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            document_id uuid NOT NULL,
            version_id uuid NOT NULL,
            version_number integer NOT NULL CHECK (version_number >= 1),
            supersedes_version_id uuid,
            order_id uuid NOT NULL,
            kind text NOT NULL CHECK (kind = 'AccountingInvoice'),
            media_type text NOT NULL CHECK (media_type = 'application/pdf'),
            file_name text NOT NULL CHECK (file_name ~ '^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,114}\.[Pp][Dd][Ff]$'),
            byte_length integer NOT NULL CHECK (byte_length BETWEEN 1 AND 10485760),
            content_sha256 text NOT NULL CHECK (content_sha256 ~ '^[a-f0-9]{64}$'),
            content bytea NOT NULL,
            issuer_name text NOT NULL CHECK (length(issuer_name) BETWEEN 1 AND 200
                AND issuer_name = btrim(issuer_name) AND issuer_name !~ '[[:cntrl:]]'),
            issuer_reference text NOT NULL CHECK (length(issuer_reference) BETWEEN 1 AND 64
                AND issuer_reference = btrim(issuer_reference) AND issuer_reference !~ '[[:cntrl:]]'),
            issued_on date NOT NULL,
            operation_key uuid NOT NULL,
            payload_hash text NOT NULL CHECK (payload_hash ~ '^[a-f0-9]{64}$'),
            admin_id uuid NOT NULL,
            session_id uuid NOT NULL,
            mfa_verified_at timestamptz NOT NULL CHECK (isfinite(mfa_verified_at)),
            uploaded_at timestamptz NOT NULL CHECK (isfinite(uploaded_at)),
            PRIMARY KEY (account_id, version_id),
            UNIQUE (account_id, operation_key),
            UNIQUE (account_id, document_id, version_number),
            FOREIGN KEY (account_id, order_id) REFERENCES public.checkout_order_acceptances(account_id, order_id),
            FOREIGN KEY (account_id, supersedes_version_id)
                REFERENCES public.billing_document_versions(account_id, version_id),
            CONSTRAINT billing_document_bytes CHECK (octet_length(content) = byte_length
                AND content_sha256 = encode(sha256(content), 'hex')
                AND substring(content FROM 1 FOR 5) = '\x255044462d'::bytea),
            CONSTRAINT billing_document_issue_date CHECK (issued_on >= DATE '2000-01-01'
                AND issued_on <= (uploaded_at AT TIME ZONE 'Asia/Kolkata')::date),
            CONSTRAINT billing_document_lineage CHECK ((version_number = 1) = (supersedes_version_id IS NULL)),
            CONSTRAINT billing_document_initial_only CHECK (version_number = 1)
        );
        CREATE UNIQUE INDEX billing_document_order_lineage
            ON public.billing_document_versions (account_id, order_id, kind) WHERE version_number = 1;
        CREATE INDEX billing_document_order
            ON public.billing_document_versions (account_id, order_id, uploaded_at, version_id);
        """;
}
