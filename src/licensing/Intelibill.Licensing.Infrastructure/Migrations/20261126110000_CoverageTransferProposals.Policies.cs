namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageTransferProposals
{
    private const string Account = "nullif(current_setting('app.billing_account_id', true), '')::uuid";
    private const string Actor = "nullif(current_setting('app.billing_actor_id', true), '')::uuid";
    private const string Transfer = "nullif(current_setting('app.coverage_transfer_id', true), '')::uuid";

    // Party facts are read by switching the transaction-local account scope, the way the exception status
    // functions do; only digests and booleans leave these functions, never another account's rows.
    private const string Functions = """
        DO $$ BEGIN
            EXECUTE format('CREATE POLICY coverage_transfer_function_read ON public.billing_accounts
                FOR SELECT TO %I USING (true)', current_user);
        END $$;

        CREATE FUNCTION public.coverage_transfer_account_exists(ac uuid) RETURNS bool
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT has_table_privilege(session_user, 'public.coverage_transfer_proposals', 'INSERT')
                AND EXISTS (SELECT FROM public.billing_accounts a WHERE a.id = ac)
        $$;

        CREATE FUNCTION public.coverage_transfer_permit_digest(ac uuid, shop uuid, affiliation uuid, at_time timestamptz)
        RETURNS text LANGUAGE sql STABLE SET search_path = pg_catalog AS $$
            SELECT encode(sha256(convert_to(coalesce(string_agg(
                permit_id::text || '/' || extract(epoch FROM not_before)::text || '/' || extract(epoch FROM valid_until)::text,
                ',' ORDER BY permit_id), ''), 'UTF8')), 'hex')
            FROM public.offline_device_observations
            WHERE account_id = ac AND shop_id = shop AND affiliation_request_id = affiliation
                AND permit_id IS NOT NULL AND valid_until > at_time
        $$;

        CREATE FUNCTION public.coverage_transfer_party_terms(ac uuid)
        RETURNS TABLE (term_id uuid, mode text, capacity int, starts_at timestamptz, ends_at timestamptz,
            paid_through_at timestamptz, access_ends_at timestamptz)
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE previous text := current_setting('app.billing_account_id', true);
        BEGIN
            PERFORM set_config('app.billing_account_id', ac::text, true);
            RETURN QUERY SELECT t.term_id, t.mode, t.capacity, t.starts_at, t.ends_at,
                    public.term_effective_paid_through(ac, t.term_id), public.term_effective_access_end(ac, t.term_id)
                FROM public.subscription_coverage_terms t WHERE t.account_id = ac ORDER BY t.term_id;
            PERFORM set_config('app.billing_account_id', coalesce(previous, ''), true);
        END $$;

        CREATE FUNCTION public.coverage_transfer_evidence_digest(source uuid, destination uuid)
        RETURNS text LANGUAGE sql VOLATILE SET search_path = pg_catalog AS $$
            SELECT encode(sha256(convert_to(
                coalesce((SELECT jsonb_agg(to_jsonb(s) ORDER BY s.term_id) FROM public.coverage_transfer_party_terms(source) s), '[]')::text
                || '|' ||
                coalesce((SELECT jsonb_agg(to_jsonb(d) ORDER BY d.term_id) FROM public.coverage_transfer_party_terms(destination) d), '[]')::text,
                'UTF8')), 'hex')
        $$;

        CREATE FUNCTION public.coverage_transfer_freeze_evidence(id uuid) RETURNS void
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE
            r public.coverage_transfer_proposals%ROWTYPE;
            previous text := current_setting('app.billing_account_id', true);
            party record;
        BEGIN
            SELECT * INTO STRICT r FROM public.coverage_transfer_proposals WHERE transfer_id = id;
            FOR party IN SELECT * FROM (VALUES ('Source', r.source_account_id), ('Destination', r.destination_account_id)) v(side, ac) LOOP
                PERFORM set_config('app.billing_account_id', party.ac::text, true);
                INSERT INTO public.coverage_transfer_evidence (transfer_id, side, account_id, term_id, mode, capacity,
                    starts_at, ends_at, paid_through_at, access_ends_at, recorded_at)
                SELECT id, party.side, party.ac, t.term_id, t.mode, t.capacity, t.starts_at, t.ends_at,
                    t.paid_through_at, t.access_ends_at, r.created_at
                FROM public.coverage_transfer_party_terms(party.ac) t;
            END LOOP;
            PERFORM set_config('app.billing_account_id', coalesce(previous, ''), true);
        END $$;

        CREATE FUNCTION public.coverage_transfer_owner_current(ac uuid, actor uuid, member uuid, rev bigint)
        RETURNS bool LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE
            previous_account text := current_setting('app.billing_account_id', true);
            previous_actor text := current_setting('app.billing_actor_id', true);
            authorized bool;
        BEGIN
            PERFORM set_config('app.billing_account_id', ac::text, true), set_config('app.billing_actor_id', actor::text, true);
            SELECT EXISTS (SELECT FROM public.billing_account_members m WHERE m.id = member AND m.account_id = ac
                AND m.merchant_user_id = actor AND m.role = 1 AND m.status = 1 AND m.revision = rev) INTO authorized;
            PERFORM set_config('app.billing_account_id', coalesce(previous_account, ''), true),
                set_config('app.billing_actor_id', coalesce(previous_actor, ''), true);
            RETURN authorized;
        END $$;

        -- NULL while the frozen proposal still matches every current fact; otherwise the first reason it does not.
        CREATE FUNCTION public.coverage_transfer_blocker(id uuid, at_time timestamptz,
            merchant_at timestamptz DEFAULT NULL, merchant_digest text DEFAULT NULL)
        RETURNS text LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE
            r public.coverage_transfer_proposals%ROWTYPE;
            previous text := current_setting('app.billing_account_id', true);
            result text;
        BEGIN
            SELECT * INTO r FROM public.coverage_transfer_proposals WHERE transfer_id = id;
            IF NOT FOUND THEN RETURN 'NotFound'; END IF;
            IF r.state = 'Retired' THEN RETURN r.retired_reason; END IF;
            IF r.expires_at <= at_time THEN RETURN 'Expired'; END IF;
            IF r.evidence_digest IS DISTINCT FROM public.coverage_transfer_evidence_digest(r.source_account_id, r.destination_account_id) THEN
                RETURN 'EvidenceChanged';
            END IF;
            PERFORM set_config('app.billing_account_id', r.source_account_id::text, true);
            SELECT CASE
                WHEN NOT EXISTS (SELECT FROM public.term_effective_roster(r.source_account_id, r.source_term_id, NULL) tr
                    WHERE tr.shop_id = r.shop_id AND tr.affiliation_request_id = r.source_affiliation_id) THEN 'SourceChanged'
                WHEN r.permit_digest IS DISTINCT FROM public.coverage_transfer_permit_digest(
                    r.source_account_id, r.shop_id, r.source_affiliation_id, r.created_at) THEN 'PermitEvidenceChanged'
                WHEN merchant_at IS NOT NULL AND merchant_digest IS DISTINCT FROM public.coverage_transfer_permit_digest(
                    r.source_account_id, r.shop_id, r.source_affiliation_id, greatest(r.created_at, merchant_at))
                    THEN 'PermitEvidenceChanged'
            END INTO result;
            PERFORM set_config('app.billing_account_id', coalesce(previous, ''), true);
            RETURN result;
        END $$;

        CREATE FUNCTION public.guard_coverage_transfer_proposal() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF (to_jsonb(NEW) - ARRAY['state','retired_reason','retired_at'])
                    IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['state','retired_reason','retired_at'])
                OR OLD.state <> 'Open' OR NEW.state <> 'Retired' THEN
                RAISE EXCEPTION 'Invalid coverage transfer proposal transition' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_coverage_transfer_proposal BEFORE UPDATE ON public.coverage_transfer_proposals
            FOR EACH ROW EXECUTE FUNCTION public.guard_coverage_transfer_proposal();
        """;

    private const string Policies = $"""
        ALTER TABLE public.coverage_transfer_proposals ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_transfer_proposals FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_transfer_evidence ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_transfer_evidence FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_transfer_consents ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_transfer_consents FORCE ROW LEVEL SECURITY;

        CREATE POLICY party_read ON public.coverage_transfer_proposals FOR SELECT USING (
            {Account} IN (source_account_id, destination_account_id) OR transfer_id = {Transfer});
        CREATE POLICY source_request ON public.coverage_transfer_proposals FOR INSERT WITH CHECK (
            source_account_id = {Account} AND requester_id = {Actor} AND state = 'Open'
            AND EXISTS (SELECT FROM public.billing_account_members m WHERE m.id = requester_member_id
                AND m.account_id = source_account_id AND m.merchant_user_id = requester_id
                AND m.role = 1 AND m.status = 1 AND m.revision = requester_revision)
            AND EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.account_id = coverage_transfer_proposals.source_account_id
                    AND u.actor_id = coverage_transfer_proposals.requester_id
                    AND u.action = 'billing.coverage-transfer.request'
                    AND u.xmin = pg_current_xact_id()::xid));
        CREATE POLICY source_retire ON public.coverage_transfer_proposals FOR UPDATE
            USING (source_account_id = {Account} AND state = 'Open')
            WITH CHECK (state = 'Retired' AND EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.account_id = coverage_transfer_proposals.source_account_id AND u.actor_id = {Actor}
                    AND u.action = 'billing.coverage-transfer.request'
                    AND u.xmin = pg_current_xact_id()::xid));

        CREATE POLICY own_read ON public.coverage_transfer_evidence FOR SELECT USING (account_id = {Account});
        CREATE POLICY freeze_insert ON public.coverage_transfer_evidence FOR INSERT WITH CHECK (
            account_id = {Account} AND EXISTS (SELECT FROM public.coverage_transfer_proposals p
                WHERE p.transfer_id = coverage_transfer_evidence.transfer_id
                    AND p.xmin = pg_current_xact_id()::xid
                    AND coverage_transfer_evidence.account_id = CASE coverage_transfer_evidence.side
                        WHEN 'Source' THEN p.source_account_id ELSE p.destination_account_id END));

        CREATE POLICY party_read ON public.coverage_transfer_consents FOR SELECT USING (
            {Account} IN (source_account_id, destination_account_id) OR transfer_id = {Transfer});
        CREATE POLICY billing_owner_insert ON public.coverage_transfer_consents FOR INSERT WITH CHECK (
            purpose <> 'ShopOwner' AND account_id = {Account} AND actor_id = {Actor}
            AND EXISTS (SELECT FROM public.billing_account_members m WHERE m.id = member_id
                AND m.account_id = coverage_transfer_consents.account_id AND m.merchant_user_id = actor_id
                AND m.role = 1 AND m.status = 1 AND m.revision = authority_revision)
            AND EXISTS (SELECT FROM public.merchant_billing_delegation_uses u
                WHERE u.account_id = coverage_transfer_consents.account_id
                    AND u.actor_id = coverage_transfer_consents.actor_id
                    AND u.action = 'billing.coverage-transfer.consent'
                    AND u.xmin = pg_current_xact_id()::xid));
        CREATE POLICY shop_owner_insert ON public.coverage_transfer_consents FOR INSERT WITH CHECK (
            purpose = 'ShopOwner' AND account_id = {Account}
            AND EXISTS (SELECT FROM public.entitlement_merchant_inbox i
                WHERE i.kind = 'CoverageTransferConsent' AND i.account_id = coverage_transfer_consents.account_id
                    AND i.event_id = coverage_transfer_consents.event_id
                    AND i.xmin = pg_current_xact_id()::xid));

        CREATE TRIGGER retain_coverage_transfer_proposal BEFORE DELETE OR TRUNCATE ON public.coverage_transfer_proposals
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_coverage_transfer_evidence BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_transfer_evidence
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        CREATE TRIGGER retain_coverage_transfer_consent BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_transfer_consents
            FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_billing_account_evidence_loss();
        """;
}
