using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

// These tables are accessed through transaction-scoped SQL, like the billing account creation fence.
[DbContext(typeof(LicensingDbContext))]
[Migration("20261115152000_AddBillingManagerInvitations")]
public sealed partial class AddBillingManagerInvitations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Tables);
        migrationBuilder.Sql(Policies);
        migrationBuilder.Sql(MemberAccess);
    }

    private const string Tables = """
        CREATE TABLE public.billing_account_invitations (
            id uuid PRIMARY KEY,
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            invited_actor_id uuid NOT NULL,
            issuer_actor_id uuid NOT NULL,
            issuer_revision integer NOT NULL CHECK (issuer_revision > 0),
            account_name varchar(120) NOT NULL,
            proof_hash bytea NOT NULL CHECK (octet_length(proof_hash) = 32),
            sealed_proof text,
            created_at timestamptz NOT NULL,
            expires_at timestamptz NOT NULL CHECK (expires_at > created_at),
            revision integer NOT NULL DEFAULT 1,
            accepted_at timestamptz,
            accepted_member_id uuid UNIQUE,
            CHECK (invited_actor_id <> issuer_actor_id),
            CHECK ((revision = 1 AND accepted_at IS NULL AND accepted_member_id IS NULL AND sealed_proof IS NOT NULL)
                OR (revision = 2 AND accepted_at IS NOT NULL AND accepted_member_id IS NOT NULL
                    AND accepted_at >= created_at AND accepted_at < expires_at AND sealed_proof IS NULL))
        );

        CREATE INDEX billing_invitation_recipient ON public.billing_account_invitations (invited_actor_id, id);
        CREATE INDEX billing_invitation_target ON public.billing_account_invitations (account_id, invited_actor_id, expires_at);

        CREATE TABLE public.billing_invitation_issue_receipts (
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            actor_id uuid NOT NULL,
            operation_key uuid NOT NULL,
            request_hash bytea NOT NULL CHECK (octet_length(request_hash) = 32),
            PRIMARY KEY (account_id, actor_id, operation_key)
        );

        CREATE TABLE public.billing_invitation_audit (
            invitation_id uuid NOT NULL REFERENCES public.billing_account_invitations(id),
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            version integer NOT NULL DEFAULT 1 CHECK (version = 1),
            actor_id uuid NOT NULL,
            request_id uuid NOT NULL,
            occurred_at timestamptz NOT NULL,
            action text NOT NULL,
            invitation_revision integer NOT NULL,
            member_id uuid,
            CHECK ((action = 'Issued' AND invitation_revision = 1 AND member_id IS NULL)
                OR (action = 'Accepted' AND invitation_revision = 2 AND member_id IS NOT NULL)),
            PRIMARY KEY (invitation_id, invitation_revision)
        );
        """;

    private const string Policies = """
        ALTER TABLE public.billing_account_invitations ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.billing_account_invitations FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.billing_invitation_issue_receipts ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.billing_invitation_issue_receipts FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.billing_invitation_audit ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.billing_invitation_audit FORCE ROW LEVEL SECURITY;

        CREATE POLICY billing_invitation_read ON public.billing_account_invitations FOR SELECT USING (
            (invited_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
                AND (nullif(current_setting('app.billing_account_id', true), '') IS NULL
                    OR account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid))
            OR (issuer_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
                AND account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid)
        );
        CREATE POLICY billing_invitation_issue ON public.billing_account_invitations FOR INSERT WITH CHECK (
            revision = 1 AND account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
            AND issuer_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.billing_account_members m WHERE m.account_id = billing_account_invitations.account_id
                AND m.merchant_user_id = issuer_actor_id AND m.role = 1 AND m.status = 1 AND m.revision = issuer_revision)
        );
        CREATE POLICY billing_invitation_accept ON public.billing_account_invitations FOR UPDATE USING (
            revision = 1 AND invited_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            AND account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
        ) WITH CHECK (
            revision = 2 AND invited_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            AND account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
        );

        CREATE POLICY billing_invitation_receipt_scope ON public.billing_invitation_issue_receipts USING (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
            AND actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.billing_account_members m
                WHERE m.account_id = billing_invitation_issue_receipts.account_id
                  AND m.merchant_user_id = actor_id AND m.role = 1 AND m.status = 1)
        );
        CREATE POLICY billing_invitation_audit_insert ON public.billing_invitation_audit FOR INSERT WITH CHECK (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
            AND actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.billing_account_invitations i WHERE i.id = invitation_id
                AND i.account_id = billing_invitation_audit.account_id AND i.revision = invitation_revision
                AND ((action = 'Issued' AND i.issuer_actor_id = actor_id AND occurred_at = i.created_at)
                    OR (action = 'Accepted' AND i.invited_actor_id = actor_id AND occurred_at = i.accepted_at
                        AND i.accepted_member_id = member_id)))
        );

        CREATE TRIGGER billing_invitation_retained BEFORE DELETE OR TRUNCATE ON public.billing_account_invitations
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER billing_invitation_receipt_retained BEFORE UPDATE OR DELETE OR TRUNCATE
            ON public.billing_invitation_issue_receipts
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER billing_invitation_audit_retained BEFORE UPDATE OR DELETE OR TRUNCATE
            ON public.billing_invitation_audit
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();

        CREATE FUNCTION public.guard_billing_invitation_transition() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF OLD.revision <> 1 OR NEW.revision <> 2 OR
                (OLD.id, OLD.account_id, OLD.invited_actor_id, OLD.issuer_actor_id, OLD.issuer_revision,
                    OLD.account_name, OLD.proof_hash, OLD.created_at, OLD.expires_at)
                IS DISTINCT FROM
                (NEW.id, NEW.account_id, NEW.invited_actor_id, NEW.issuer_actor_id, NEW.issuer_revision,
                    NEW.account_name, NEW.proof_hash, NEW.created_at, NEW.expires_at) THEN
                RAISE EXCEPTION 'Invalid billing invitation transition' USING ERRCODE = '55000';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER billing_invitation_transition BEFORE UPDATE ON public.billing_account_invitations
            FOR EACH ROW EXECUTE FUNCTION public.guard_billing_invitation_transition();
        """;

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            LOCK TABLE public.billing_account_invitations, public.billing_invitation_audit,
                public.billing_invitation_issue_receipts IN ACCESS EXCLUSIVE MODE;
            ALTER TABLE public.billing_account_invitations NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_invitation_audit NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE public.billing_invitation_issue_receipts NO FORCE ROW LEVEL SECURITY;
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM public.billing_account_invitations)
                    OR EXISTS (SELECT FROM public.billing_invitation_audit)
                    OR EXISTS (SELECT FROM public.billing_invitation_issue_receipts) THEN
                    RAISE EXCEPTION 'Billing invitation evidence exists' USING ERRCODE = '55000';
                END IF;
            END $$;
            DROP POLICY billing_account_members_insert_manager ON public.billing_account_members;
            DROP POLICY billing_member_function_read ON public.billing_account_members;
            DROP POLICY billing_member_function_lock ON public.billing_account_members;
            DROP FUNCTION public.billing_roster(uuid, uuid);
            DROP FUNCTION public.billing_inviter_is_current(uuid);
            DROP TABLE public.billing_invitation_audit, public.billing_invitation_issue_receipts, public.billing_account_invitations;
            DROP FUNCTION public.guard_billing_invitation_transition();
            DROP FUNCTION public.check_billing_invitation_grant();
            """);
    }
}
