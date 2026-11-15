using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Licensing.Infrastructure.Migrations;

[DbContext(typeof(LicensingDbContext))]
[Migration("20261116120000_AddBillingOwnershipTransfers")]
public sealed partial class AddBillingOwnershipTransfers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Proposals);
        migrationBuilder.Sql(Acceptances);
        migrationBuilder.Sql(OwnerInvariant);
    }

    private const string Proposals = """
        CREATE TABLE public.billing_ownership_proposals (
            id uuid PRIMARY KEY,
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            account_name varchar(120) NOT NULL,
            owner_actor_id uuid NOT NULL,
            owner_member_id uuid NOT NULL,
            owner_revision integer NOT NULL CHECK (owner_revision > 0),
            recipient_actor_id uuid CHECK (recipient_actor_id <> owner_actor_id),
            recipient_memberships jsonb,
            operation_key uuid NOT NULL,
            request_id uuid NOT NULL,
            request_hash bytea NOT NULL CHECK (octet_length(request_hash) = 32),
            reason text NOT NULL CHECK (char_length(reason) BETWEEN 1 AND 500),
            responsibility_policy_version text NOT NULL CHECK (responsibility_policy_version = 'prospective-billing-owner/1'),
            created_at timestamptz NOT NULL,
            expires_at timestamptz NOT NULL CHECK (expires_at > created_at),
            CHECK ((recipient_actor_id IS NULL) = (recipient_memberships IS NULL)),
            UNIQUE (owner_actor_id, operation_key)
        );

        CREATE INDEX ix_billing_ownership_proposal_recipient
            ON public.billing_ownership_proposals (recipient_actor_id, id) WHERE recipient_actor_id IS NOT NULL;
        ALTER TABLE public.billing_ownership_proposals ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.billing_ownership_proposals FORCE ROW LEVEL SECURITY;

        CREATE POLICY billing_transfer_proposal_read ON public.billing_ownership_proposals FOR SELECT USING (
            nullif(current_setting('app.billing_actor_id', true), '')::uuid IN (owner_actor_id, recipient_actor_id)
            AND (nullif(current_setting('app.billing_account_id', true), '') IS NULL
                OR account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid)
        );

        DO $$
        BEGIN
            EXECUTE format('CREATE POLICY billing_transfer_proposal_write ON public.billing_ownership_proposals
                FOR INSERT TO %I WITH CHECK (true)', current_user);
        END $$;
        CREATE TRIGGER billing_transfer_proposal_retained BEFORE UPDATE OR DELETE OR TRUNCATE
            ON public.billing_ownership_proposals FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();

        CREATE FUNCTION public.billing_transfer_membership_snapshot(p_account uuid, p_actor uuid) RETURNS jsonb
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT coalesce(jsonb_agg(jsonb_build_array(id, role, status, revision) ORDER BY id), '[]'::jsonb)
            FROM public.billing_account_members WHERE account_id = p_account AND merchant_user_id = p_actor
        $$;
        REVOKE ALL ON FUNCTION public.billing_transfer_membership_snapshot(uuid, uuid) FROM PUBLIC;

        CREATE FUNCTION public.billing_transfer_has_proof(p_request uuid, p_actor uuid, p_account uuid,
            p_action text, p_at timestamptz) RETURNS boolean
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.request_id = p_request AND u.actor_id = p_actor
                  AND u.account_id IS NOT DISTINCT FROM p_account AND u.action = p_action
                  AND u.issued_at <= p_at AND p_at < u.expires_at
                  AND u.xmin = pg_current_xact_id()::xid)
        $$;
        REVOKE ALL ON FUNCTION public.billing_transfer_has_proof(uuid, uuid, uuid, text, timestamptz) FROM PUBLIC;

        CREATE FUNCTION public.issue_billing_ownership_transfer(p_id uuid, p_recipient uuid, p_version integer,
            p_key uuid, p_request uuid, p_hash bytea, p_reason text, p_policy text, p_at timestamptz,
            p_until timestamptz) RETURNS boolean
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $$
        DECLARE
            account uuid := nullif(current_setting('app.billing_account_id', true), '')::uuid;
            actor uuid := nullif(current_setting('app.billing_actor_id', true), '')::uuid;
            owner_member uuid;
            recipient uuid := nullif(p_recipient, actor);
        BEGIN
            IF NOT has_table_privilege(session_user, 'public.billing_ownership_proposals', 'INSERT')
                OR NOT public.billing_transfer_has_proof(p_request, actor, account, 'billing.ownership-transfer.issue', p_at)
                THEN RETURN false;
            END IF;
            SELECT m.id INTO owner_member FROM public.billing_account_members m
                WHERE m.account_id = account AND m.merchant_user_id = actor AND m.role = 1
                  AND m.status = 1 AND m.revision = p_version FOR SHARE;
            IF NOT FOUND THEN RETURN false; END IF;
            INSERT INTO public.billing_ownership_proposals
                (id, account_id, account_name, owner_actor_id, owner_member_id, owner_revision,
                 recipient_actor_id, recipient_memberships, operation_key, request_id, request_hash, reason,
                 responsibility_policy_version, created_at, expires_at)
            SELECT p_id, a.id, a.name, actor, owner_member, p_version, recipient,
                   CASE WHEN recipient IS NOT NULL THEN public.billing_transfer_membership_snapshot(account, recipient) END,
                   p_key, p_request, p_hash, p_reason, p_policy, p_at, p_until
            FROM public.billing_accounts a WHERE a.id = account
            ON CONFLICT (owner_actor_id, operation_key) DO NOTHING;
            RETURN true;
        END $$;
        """;

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public.billing_ownership_proposals IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.billing_ownership_proposals NO FORCE ROW LEVEL SECURITY;
        DO $$
        BEGIN
            IF EXISTS (SELECT FROM public.billing_ownership_proposals) THEN
                RAISE EXCEPTION 'Billing ownership transfer evidence exists' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TRIGGER billing_member_owner_required ON public.billing_account_members;
        DROP TRIGGER billing_account_owner_required ON public.billing_accounts;
        DROP FUNCTION public.check_billing_active_owner();
        DROP POLICY billing_transfer_member_swap ON public.billing_account_members;
        DROP POLICY billing_transfer_acceptance_proof ON public.merchant_billing_delegation_uses;
        DROP FUNCTION public.accept_billing_ownership_transfer(uuid, uuid, uuid, uuid, bytea, text, timestamptz);
        DROP TABLE public.billing_ownership_acceptances;
        DROP FUNCTION public.issue_billing_ownership_transfer(uuid, uuid, integer, uuid, uuid, bytea, text, text, timestamptz, timestamptz);
        DROP FUNCTION public.billing_transfer_membership_snapshot(uuid, uuid);
        DROP FUNCTION public.billing_transfer_has_proof(uuid, uuid, uuid, text, timestamptz);
        DROP TABLE public.billing_ownership_proposals;
        """);
}
