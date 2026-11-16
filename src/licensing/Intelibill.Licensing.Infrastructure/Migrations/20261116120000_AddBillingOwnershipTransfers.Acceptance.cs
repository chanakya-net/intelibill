namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class AddBillingOwnershipTransfers
{
    private const string Acceptances = """
        CREATE TABLE public.billing_ownership_acceptances (
            transfer_id uuid PRIMARY KEY REFERENCES public.billing_ownership_proposals(id),
            account_id uuid NOT NULL REFERENCES public.billing_accounts(id),
            recipient_actor_id uuid NOT NULL,
            recipient_member_id uuid NOT NULL,
            recipient_before_revision integer NOT NULL CHECK (recipient_before_revision >= 0),
            recipient_after_revision integer NOT NULL CHECK (recipient_after_revision = recipient_before_revision + 1),
            owner_after_revision integer NOT NULL CHECK (owner_after_revision > 1),
            operation_key uuid NOT NULL,
            request_id uuid NOT NULL,
            request_hash bytea NOT NULL CHECK (octet_length(request_hash) = 32),
            effective_at timestamptz NOT NULL,
            UNIQUE (recipient_actor_id, operation_key)
        );
        -- A proposal and its unique acceptance together preserve the parties, both member revisions,
        -- both request identities and hashes, the consent disclosure and the effective boundary.
        ALTER TABLE public.billing_ownership_acceptances ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.billing_ownership_acceptances FORCE ROW LEVEL SECURITY;
        CREATE POLICY billing_transfer_acceptance_read ON public.billing_ownership_acceptances FOR SELECT USING (
            recipient_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            AND (nullif(current_setting('app.billing_account_id', true), '') IS NULL
                OR account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid)
        );
        CREATE TRIGGER billing_transfer_acceptance_retained BEFORE UPDATE OR DELETE OR TRUNCATE
            ON public.billing_ownership_acceptances FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        DO $$
        BEGIN
            EXECUTE format('CREATE POLICY billing_transfer_acceptance_write ON public.billing_ownership_acceptances
                FOR INSERT TO %I WITH CHECK (true)', current_user);
            EXECUTE format('CREATE POLICY billing_transfer_acceptance_proof ON public.merchant_billing_delegation_uses
                FOR SELECT TO %I USING (account_id IS NULL AND action = ''billing.ownership-transfer.accept''
                    AND actor_id = nullif(current_setting(''app.billing_actor_id'', true), '''')::uuid)', current_user);
            EXECUTE format('CREATE POLICY billing_transfer_member_swap ON public.billing_account_members FOR UPDATE TO %I
                USING (account_id = nullif(current_setting(''app.billing_account_id'', true), '''')::uuid AND status = 1)
                WITH CHECK (account_id = nullif(current_setting(''app.billing_account_id'', true), '''')::uuid AND status = 1)', current_user);
        END $$;

        CREATE FUNCTION public.accept_billing_ownership_transfer(p_transfer uuid, p_member uuid, p_key uuid,
            p_request uuid, p_hash bytea, p_policy text, p_at timestamptz) RETURNS boolean
        LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $$
        DECLARE
            account uuid := nullif(current_setting('app.billing_account_id', true), '')::uuid;
            actor uuid := nullif(current_setting('app.billing_actor_id', true), '')::uuid;
            proposal public.billing_ownership_proposals%ROWTYPE;
            recipient public.billing_account_members%ROWTYPE;
        BEGIN
            IF NOT has_table_privilege(session_user, 'public.billing_ownership_acceptances', 'INSERT')
                OR NOT public.billing_transfer_has_proof(p_request, actor, NULL, 'billing.ownership-transfer.accept', p_at)
                THEN RETURN false;
            END IF;

            SELECT * INTO proposal FROM public.billing_ownership_proposals
            WHERE id = p_transfer AND account_id = account AND recipient_actor_id = actor
              AND created_at <= p_at AND p_at < expires_at AND responsibility_policy_version = p_policy;
            IF NOT FOUND OR EXISTS (SELECT FROM public.billing_ownership_acceptances WHERE transfer_id = p_transfer)
                OR proposal.recipient_memberships IS DISTINCT FROM public.billing_transfer_membership_snapshot(account, actor)
                THEN RETURN false;
            END IF;

            UPDATE public.billing_account_members SET role = 2, revision = revision + 1
            WHERE account_id = account AND id = proposal.owner_member_id AND merchant_user_id = proposal.owner_actor_id
              AND role = 1 AND status = 1 AND revision = proposal.owner_revision;
            IF NOT FOUND THEN RETURN false; END IF;

            SELECT * INTO recipient FROM public.billing_account_members
            WHERE account_id = account AND merchant_user_id = actor AND status = 1;
            IF FOUND THEN
                UPDATE public.billing_account_members SET role = 1, revision = revision + 1
                WHERE account_id = account AND id = recipient.id AND role = 2 AND status = 1 AND revision = recipient.revision;
                IF NOT FOUND THEN RETURN false; END IF;
            ELSE
                INSERT INTO public.billing_account_members
                    (id, account_id, merchant_user_id, role, status, revision, created_at)
                VALUES (p_member, account, actor, 1, 1, 1, p_at);
            END IF;

            INSERT INTO public.billing_ownership_acceptances
                (transfer_id, account_id, recipient_actor_id, recipient_member_id, recipient_before_revision,
                 recipient_after_revision, owner_after_revision, operation_key, request_id, request_hash, effective_at)
            VALUES (p_transfer, account, actor, coalesce(recipient.id, p_member), coalesce(recipient.revision, 0),
                coalesce(recipient.revision, 0) + 1, proposal.owner_revision + 1, p_key, p_request, p_hash, p_at)
            ON CONFLICT DO NOTHING;
            -- A false result always rolls back the delegating transaction, including any tentative role changes.
            RETURN FOUND;
        END $$;
        """;

    private const string OwnerInvariant = """
        LOCK TABLE public.billing_accounts, public.billing_account_members IN SHARE ROW EXCLUSIVE MODE;
        ALTER TABLE public.billing_accounts NO FORCE ROW LEVEL SECURITY;
        DO $$
        BEGIN
            IF EXISTS (SELECT FROM public.billing_accounts a WHERE NOT EXISTS (SELECT FROM public.billing_account_members m
                        WHERE m.account_id = a.id AND m.role = 1 AND m.status = 1)) THEN
                RAISE EXCEPTION 'Billing account exists without an active owner' USING ERRCODE = '23514';
            END IF;
        END $$;
        ALTER TABLE public.billing_accounts FORCE ROW LEVEL SECURITY;

        CREATE FUNCTION public.check_billing_active_owner() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $$
        DECLARE
            account uuid;
        BEGIN
            IF TG_TABLE_NAME = 'billing_accounts' THEN
                account := NEW.id;
            ELSIF TG_OP = 'UPDATE' THEN
                IF NEW.account_id <> OLD.account_id THEN
                    RAISE EXCEPTION 'Billing member account is immutable' USING ERRCODE = '23514';
                END IF;
                account := NEW.account_id;
            ELSE
                account := NEW.account_id;
            END IF;
            IF NOT EXISTS (SELECT FROM public.billing_account_members
                           WHERE account_id = account AND role = 1 AND status = 1) THEN
                RAISE EXCEPTION 'Billing account requires an active owner' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER billing_account_owner_required AFTER INSERT ON public.billing_accounts
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.check_billing_active_owner();
        CREATE CONSTRAINT TRIGGER billing_member_owner_required AFTER INSERT OR UPDATE ON public.billing_account_members
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.check_billing_active_owner();
        """;
}
