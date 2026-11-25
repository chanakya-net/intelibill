using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Intelibill.Infrastructure.Migrations;

/// <summary>
/// P06-06: Merchant-side two-party shop-owner consent events, prepare/consume/abort decisions, durable outgoing
/// permit/trading cutoffs, per-leg activation evidence and permit INSERT cutoff enforcement.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261125140000_AddMerchantCoverageExceptionEvidence")]
public sealed class AddMerchantCoverageExceptionEvidence : Migration
{
    private const string PreviousKinds =
        "'Snapshot','Acknowledgement','Reconciliation','DeviceObservation','CapacityReceipt','ReductionReceipt','CoverageSlotConsent','CoverageReplacementConsent'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
                ADD CHECK (kind IN ({PreviousKinds},'CoverageReplacementExceptionConsent','CoverageReplacementExceptionReceipt'));
            """);
        migrationBuilder.Sql("""
        CREATE TABLE public.coverage_exception_consent_events (
            id uuid PRIMARY KEY,
            shop_id uuid NOT NULL REFERENCES public.shops(id),
            account_id uuid NOT NULL,
            exception_id uuid NOT NULL,
            proposal_revision bigint NOT NULL CHECK (proposal_revision > 0),
            terms_digest text NOT NULL CHECK (terms_digest ~ '^[0-9a-f]{64}$'),
            leg text NOT NULL CHECK (leg IN ('Outgoing', 'Incoming')),
            affiliation_request_id uuid NOT NULL,
            term_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            membership_id uuid NOT NULL,
            authority_version bigint NOT NULL CHECK (authority_version >= 0),
            operation_key uuid NOT NULL,
            kind text NOT NULL CHECK (kind IN ('Granted', 'Revoked', 'Accepted')),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            expires_at timestamptz NOT NULL CHECK (isfinite(expires_at) AND (kind = 'Revoked' OR recorded_at < expires_at)),
            requested_early_start_at timestamptz NOT NULL CHECK (isfinite(requested_early_start_at)),
            paid_cutover_at timestamptz NOT NULL CHECK (isfinite(paid_cutover_at)),
            normal_renewal_at timestamptz NOT NULL CHECK (isfinite(normal_renewal_at)),
            overlap_disposition text NOT NULL CHECK (overlap_disposition IN ('Wait', 'CourtesyBridge')),
            member_allowance int NOT NULL CHECK (member_allowance > 0),
            active_members int,
            seat_hash text,
            CHECK (requested_early_start_at <= paid_cutover_at AND paid_cutover_at < normal_renewal_at),
            CHECK ((kind = 'Accepted') = (active_members IS NOT NULL)),
            CHECK ((kind = 'Accepted') = (seat_hash IS NOT NULL)),
            CHECK (active_members IS NULL OR active_members >= 1),
            CHECK (leg <> 'Incoming' OR active_members IS NULL OR active_members <= member_allowance),
            CHECK (seat_hash IS NULL OR seat_hash ~ '^[0-9a-f]{64}$'),
            UNIQUE (exception_id, leg, kind),
            UNIQUE (shop_id, actor_id, operation_key)
        );

        CREATE TABLE public.merchant_coverage_exception_decisions (
            event_id uuid PRIMARY KEY,
            exception_id uuid NOT NULL,
            approval_id uuid NOT NULL,
            generation bigint NOT NULL CHECK (generation > 0),
            phase text NOT NULL CHECK (phase IN ('CoverageExceptionPrepare', 'CoverageExceptionConsume', 'CoverageExceptionAbort')),
            account_id uuid NOT NULL,
            term_id uuid NOT NULL,
            outgoing_shop_id uuid NOT NULL REFERENCES public.shops(id),
            outgoing_affiliation_id uuid NOT NULL,
            incoming_shop_id uuid NOT NULL REFERENCES public.shops(id),
            incoming_affiliation_id uuid NOT NULL,
            requested_early_start_at timestamptz NOT NULL CHECK (isfinite(requested_early_start_at)),
            paid_cutover_at timestamptz NOT NULL CHECK (isfinite(paid_cutover_at)),
            normal_renewal_at timestamptz NOT NULL CHECK (isfinite(normal_renewal_at)),
            overlap_disposition text NOT NULL CHECK (overlap_disposition IN ('Wait', 'CourtesyBridge')),
            member_allowance int NOT NULL CHECK (member_allowance > 0),
            expires_at timestamptz NOT NULL CHECK (isfinite(expires_at)),
            digest text NOT NULL CHECK (digest ~ '^[0-9a-f]{64}$'),
            permit_count int NOT NULL CHECK (permit_count >= 0),
            max_permit_valid_until timestamptz CHECK (max_permit_valid_until IS NULL OR isfinite(max_permit_valid_until)),
            permit_digest text NOT NULL CHECK (permit_digest ~ '^[0-9a-f]{64}$'),
            fence_hash text NOT NULL CHECK (fence_hash ~ '^[0-9a-f]{64}$'),
            outcome text NOT NULL CHECK (outcome IN ('Prepared', 'Consumed', 'Aborted', 'Refused')),
            reason text CHECK (reason IS NULL OR length(reason) BETWEEN 1 AND 80),
            command bytea NOT NULL CHECK (octet_length(command) BETWEEN 1 AND 8192),
            assertion text NOT NULL,
            public_key text NOT NULL,
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at)),
            CHECK (outgoing_shop_id <> incoming_shop_id),
            CHECK (requested_early_start_at <= paid_cutover_at AND paid_cutover_at < normal_renewal_at),
            CHECK ((phase = 'CoverageExceptionPrepare' OR outcome <> 'Prepared')
                AND (phase = 'CoverageExceptionConsume' OR outcome <> 'Consumed')
                AND (phase = 'CoverageExceptionAbort' OR outcome <> 'Aborted')),
            UNIQUE (exception_id, generation, phase)
        );
        CREATE UNIQUE INDEX merchant_exception_single_consume ON public.merchant_coverage_exception_decisions(exception_id)
            WHERE outcome = 'Consumed';

        CREATE TABLE public.merchant_coverage_exception_cutoffs (
            exception_id uuid PRIMARY KEY,
            generation bigint NOT NULL CHECK (generation > 0),
            account_id uuid NOT NULL,
            term_id uuid NOT NULL,
            shop_id uuid NOT NULL REFERENCES public.shops(id),
            affiliation_request_id uuid NOT NULL,
            cutoff_at timestamptz NOT NULL CHECK (isfinite(cutoff_at)),
            decision_event_id uuid NOT NULL UNIQUE REFERENCES public.merchant_coverage_exception_decisions(event_id),
            recorded_at timestamptz NOT NULL CHECK (isfinite(recorded_at))
        );
        CREATE INDEX merchant_exception_cutoffs_shop ON public.merchant_coverage_exception_cutoffs(shop_id, account_id, term_id);

        CREATE TABLE public.merchant_coverage_exception_activations (
            exception_id uuid NOT NULL,
            leg text NOT NULL CHECK (leg IN ('Outgoing', 'IncomingPaid', 'IncomingCourtesy')),
            generation bigint NOT NULL CHECK (generation > 0),
            shop_id uuid NOT NULL REFERENCES public.shops(id),
            account_id uuid NOT NULL,
            event_id uuid NOT NULL UNIQUE,
            shop_revision bigint NOT NULL CHECK (shop_revision > 0),
            activated_at timestamptz NOT NULL CHECK (isfinite(activated_at)),
            PRIMARY KEY (exception_id, leg)
        );

        ALTER TABLE public.coverage_exception_consent_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.coverage_exception_consent_events FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_coverage_exception_decisions ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_coverage_exception_decisions FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_coverage_exception_cutoffs ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_coverage_exception_cutoffs FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_coverage_exception_activations ENABLE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_coverage_exception_activations FORCE ROW LEVEL SECURITY;

        CREATE POLICY shop_read ON public.coverage_exception_consent_events FOR SELECT USING (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid);
        CREATE POLICY owner_insert ON public.coverage_exception_consent_events FOR INSERT WITH CHECK (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid
            AND actor_id = nullif(current_setting('app.current_user_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.shop_memberships m WHERE m.user_id = actor_id AND m.id = membership_id
                AND m.shop_id = coverage_exception_consent_events.shop_id
                AND m.authority_version = coverage_exception_consent_events.authority_version
                AND m.role = 'Owner' AND m.status = 'Active'));

        CREATE POLICY decision_scope ON public.merchant_coverage_exception_decisions
            USING (account_id = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
                OR nullif(current_setting('app.active_shop_id', true), '')::uuid IN (outgoing_shop_id, incoming_shop_id))
            WITH CHECK (account_id = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
                AND EXISTS (SELECT FROM public.entitlement_request_uses u WHERE u.xmin = pg_current_xact_id()::xid));

        CREATE POLICY cutoff_read ON public.merchant_coverage_exception_cutoffs FOR SELECT USING (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid
            OR account_id = nullif(current_setting('app.entitlement_account_id', true), '')::uuid);
        CREATE POLICY cutoff_insert ON public.merchant_coverage_exception_cutoffs FOR INSERT WITH CHECK (
            account_id = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
            AND EXISTS (SELECT FROM public.merchant_coverage_exception_decisions d
                WHERE d.event_id = merchant_coverage_exception_cutoffs.decision_event_id
                    AND d.exception_id = merchant_coverage_exception_cutoffs.exception_id
                    AND d.generation = merchant_coverage_exception_cutoffs.generation
                    AND d.account_id = merchant_coverage_exception_cutoffs.account_id
                    AND d.outgoing_shop_id = merchant_coverage_exception_cutoffs.shop_id
                    AND d.outcome = 'Consumed' AND d.xmin = pg_current_xact_id()::xid));

        CREATE POLICY activation_read ON public.merchant_coverage_exception_activations FOR SELECT USING (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid);
        CREATE POLICY activation_insert ON public.merchant_coverage_exception_activations FOR INSERT WITH CHECK (
            shop_id = nullif(current_setting('app.active_shop_id', true), '')::uuid);

        CREATE TRIGGER retain_exception_consent BEFORE UPDATE OR DELETE OR TRUNCATE ON public.coverage_exception_consent_events
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_shop_affiliation_evidence();
        CREATE TRIGGER retain_exception_decision BEFORE UPDATE OR DELETE OR TRUNCATE ON public.merchant_coverage_exception_decisions
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_exception_cutoff BEFORE UPDATE OR DELETE OR TRUNCATE ON public.merchant_coverage_exception_cutoffs
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();
        CREATE TRIGGER retain_exception_activation BEFORE UPDATE OR DELETE OR TRUNCATE ON public.merchant_coverage_exception_activations
            FOR EACH STATEMENT EXECUTE FUNCTION public.retain_entitlement_evidence();

        -- Enforce durable source cutoff directly at offline permit INSERT under the same shop fence.
        CREATE FUNCTION public.guard_offline_permit_exception_cutoff() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF EXISTS (
                SELECT FROM public.merchant_coverage_exception_cutoffs c
                JOIN public.offline_license_grants g ON g.id = NEW.grant_id
                    AND g.shop_id = c.shop_id AND g.account_id = c.account_id
                    AND g.affiliation_request_id = c.affiliation_request_id
                WHERE c.shop_id = NEW.shop_id AND (NEW.not_before >= c.cutoff_at OR NEW.valid_until > c.cutoff_at)
            ) THEN
                RAISE EXCEPTION 'Offline permit exceeds coverage exception cutoff' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER guard_offline_permit_exception_cutoff BEFORE INSERT ON public.offline_license_permits
            FOR EACH ROW EXECUTE FUNCTION public.guard_offline_permit_exception_cutoff();

        -- Runs inside the exclusive account + both-shops fence after replay consumption. Reads both shops' bindings,
        -- live owner consents, incoming seat count and ALL retained outgoing offline permits without disclosing raw
        -- device IDs or signed permit tokens.
        CREATE FUNCTION public.exception_fence_inspect(
            p_account uuid, p_term uuid, p_exception uuid, p_proposal_rev bigint, p_terms_digest text,
            p_out_shop uuid, p_out_aff uuid, p_in_shop uuid, p_in_aff uuid,
            p_out_event uuid, p_in_event uuid, p_allowance int, p_at timestamptz)
        RETURNS TABLE (
            outgoing_bound bool,
            incoming_bound bool,
            outgoing_consent_valid bool,
            incoming_consent_valid bool,
            incoming_active_members int,
            outgoing_permit_count int,
            max_permit_valid_until timestamptz,
            permit_digest text,
            fence_hash text
        ) LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            WITH gate AS (
                SELECT p_account = nullif(current_setting('app.entitlement_account_id', true), '')::uuid
                    AND EXISTS (SELECT FROM public.entitlement_request_uses u
                        WHERE u.replay_id = nullif(current_setting('app.entitlement_replay_id', true), '')::uuid
                            AND u.xmin = pg_current_xact_id()::xid) AS ok
            ), out_bind AS (
                SELECT EXISTS (
                    SELECT FROM public.shop_affiliation_consent_events e
                    LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = e.id
                    JOIN public.shop_license_states s ON s.shop_id = e.shop_id AND s.account_id = e.account_id
                        AND s.affiliation_request_id = e.request_id
                    WHERE e.shop_id = p_out_shop AND e.account_id = p_account AND e.request_id = p_out_aff
                        AND e.kind = 'Accepted' AND r.status IS DISTINCT FROM 'NeedsReview'
                        AND s.state <> 'Closed'
                ) AS bound,
                coalesce((SELECT s.revision FROM public.shop_license_states s WHERE s.shop_id = p_out_shop), 0) AS rev
            ), in_bind AS (
                SELECT EXISTS (
                    SELECT FROM public.shop_affiliation_consent_events e
                    LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = e.id
                    LEFT JOIN public.shop_license_states s ON s.shop_id = e.shop_id
                    WHERE e.shop_id = p_in_shop AND e.account_id = p_account AND e.request_id = p_in_aff
                        AND e.kind = 'Accepted' AND r.status IS DISTINCT FROM 'NeedsReview'
                        AND (s.shop_id IS NULL OR (s.account_id = p_account AND s.affiliation_request_id = p_in_aff
                            AND s.state <> 'Closed'))
                ) AS bound,
                coalesce((SELECT s.revision FROM public.shop_license_states s WHERE s.shop_id = p_in_shop), 0) AS rev
            ), out_consent AS (
                SELECT EXISTS (
                    SELECT FROM public.coverage_exception_consent_events e
                    JOIN public.shop_memberships m ON m.shop_id = e.shop_id AND m.id = e.membership_id
                        AND m.user_id = e.actor_id
                    WHERE e.id = p_out_event AND e.exception_id = p_exception AND e.leg = 'Outgoing'
                        AND e.kind = 'Accepted' AND e.account_id = p_account AND e.term_id = p_term
                        AND e.shop_id = p_out_shop AND e.affiliation_request_id = p_out_aff
                        AND e.proposal_revision = p_proposal_rev AND e.terms_digest = p_terms_digest
                        AND m.authority_version = e.authority_version AND m.role = 'Owner' AND m.status = 'Active'
                        AND NOT EXISTS (SELECT FROM public.coverage_exception_consent_events v
                            WHERE v.exception_id = p_exception AND v.leg = 'Outgoing' AND v.kind = 'Revoked')
                ) AS valid
            ), in_consent AS (
                SELECT EXISTS (
                    SELECT FROM public.coverage_exception_consent_events e
                    JOIN public.shop_memberships m ON m.shop_id = e.shop_id AND m.id = e.membership_id
                        AND m.user_id = e.actor_id
                    WHERE e.id = p_in_event AND e.exception_id = p_exception AND e.leg = 'Incoming'
                        AND e.kind = 'Accepted' AND e.account_id = p_account AND e.term_id = p_term
                        AND e.shop_id = p_in_shop AND e.affiliation_request_id = p_in_aff
                        AND e.proposal_revision = p_proposal_rev AND e.terms_digest = p_terms_digest
                        AND e.member_allowance = p_allowance
                        AND m.authority_version = e.authority_version AND m.role = 'Owner' AND m.status = 'Active'
                        AND NOT EXISTS (SELECT FROM public.coverage_exception_consent_events v
                            WHERE v.exception_id = p_exception AND v.leg = 'Incoming' AND v.kind = 'Revoked')
                ) AS valid
            ), in_seats AS (
                SELECT count(*) FILTER (WHERE m.status = 'Active')::int AS active_members,
                    encode(sha256(convert_to(coalesce(string_agg(m.id::text || '/' || m.authority_version::text || '/' || m.status::text, ','
                        ORDER BY m.id), ''), 'UTF8')), 'hex') AS seat_hash
                FROM public.shop_memberships m WHERE m.shop_id = p_in_shop
            ), out_seats AS (
                SELECT encode(sha256(convert_to(coalesce(string_agg(m.id::text || '/' || m.authority_version::text || '/' || m.status::text, ','
                    ORDER BY m.id), ''), 'UTF8')), 'hex') AS seat_hash
                FROM public.shop_memberships m WHERE m.shop_id = p_out_shop
            ), permits AS (
                SELECT count(*)::int AS cnt, max(p.valid_until) AS max_end,
                    encode(sha256(convert_to(coalesce(string_agg(p.id::text || '/' || to_char(p.valid_until AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'), ','
                        ORDER BY p.id), 'none'), 'UTF8')), 'hex') AS digest
                FROM public.offline_license_permits p
                WHERE p.shop_id = p_out_shop AND p.valid_until > p_at
            )
            SELECT ob.bound, ib.bound, oc.valid, ic.valid, s_in.active_members,
                pm.cnt, pm.max_end, pm.digest,
                encode(sha256(convert_to(
                    ob.rev::text || '/' || ib.rev::text || '/' || s_out.seat_hash || '/' || s_in.seat_hash || '/' || pm.digest || '/'
                    || coalesce((SELECT revision FROM public.account_license_restrictions WHERE account_id = p_account), 0)::text,
                    'UTF8')), 'hex')
            FROM gate g CROSS JOIN out_bind ob CROSS JOIN in_bind ib CROSS JOIN out_consent oc
            CROSS JOIN in_consent ic CROSS JOIN in_seats s_in CROSS JOIN out_seats s_out CROSS JOIN permits pm
            WHERE g.ok
        $$;

        -- Verifies outgoing release, durable cutoff and actual permit expiry under the caller's exclusive account fence.
        CREATE FUNCTION public.exception_outgoing_released(p_exception uuid, p_generation bigint, p_at timestamptz)
        RETURNS bool LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE
            dec public.merchant_coverage_exception_decisions%ROWTYPE;
            prior_scope text := current_setting('app.active_shop_id', true);
            released bool;
        BEGIN
            SELECT * INTO dec FROM public.merchant_coverage_exception_decisions d
            WHERE d.exception_id = p_exception AND d.generation = p_generation AND d.outcome = 'Consumed'
                AND d.incoming_shop_id = nullif(prior_scope, '')::uuid;
            IF NOT FOUND OR dec.paid_cutover_at > p_at THEN RETURN false; END IF;
            BEGIN
                PERFORM set_config('app.active_shop_id', dec.outgoing_shop_id::text, true);
                SELECT EXISTS (
                        SELECT FROM public.merchant_coverage_exception_cutoffs c
                        WHERE c.exception_id = p_exception AND c.generation = p_generation
                            AND c.shop_id = dec.outgoing_shop_id AND c.cutoff_at <= p_at)
                    AND EXISTS (
                        SELECT FROM public.shop_license_states s
                        JOIN public.license_grant_evidence e ON e.event_id = s.evidence_id
                        WHERE s.shop_id = dec.outgoing_shop_id AND s.account_id = dec.account_id
                            AND convert_from(e.payload, 'UTF8')::jsonb->'shop'->>'replacementExceptionId' = p_exception::text
                            AND convert_from(e.payload, 'UTF8')::jsonb->'shop'->>'replacementExceptionLeg' = 'Outgoing'
                            AND (convert_from(e.payload, 'UTF8')::jsonb->'shop'->>'replacementExceptionGeneration')::bigint = p_generation
                            AND (s.state NOT IN ('Active', 'Grace') OR s.access_ends_at <= p_at))
                    AND NOT EXISTS (
                        SELECT FROM public.offline_license_permits p
                        WHERE p.shop_id = dec.outgoing_shop_id AND p.valid_until > p_at)
                    INTO released;
                PERFORM set_config('app.active_shop_id', coalesce(prior_scope, ''), true);
                RETURN released;
            EXCEPTION WHEN OTHERS THEN
                PERFORM set_config('app.active_shop_id', coalesce(prior_scope, ''), true);
                RAISE;
            END;
        END $$;
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql($"""
        LOCK TABLE public.coverage_exception_consent_events, public.merchant_coverage_exception_decisions,
            public.merchant_coverage_exception_cutoffs, public.merchant_coverage_exception_activations IN ACCESS EXCLUSIVE MODE;
        ALTER TABLE public.coverage_exception_consent_events NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_coverage_exception_decisions NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_coverage_exception_cutoffs NO FORCE ROW LEVEL SECURITY;
        ALTER TABLE public.merchant_coverage_exception_activations NO FORCE ROW LEVEL SECURITY;
        DO $$ BEGIN
            IF EXISTS (SELECT FROM public.coverage_exception_consent_events)
                OR EXISTS (SELECT FROM public.merchant_coverage_exception_decisions)
                OR EXISTS (SELECT FROM public.merchant_coverage_exception_cutoffs)
                OR EXISTS (SELECT FROM public.merchant_coverage_exception_activations) THEN
                RAISE EXCEPTION 'Retained coverage exception evidence prevents downgrade' USING ERRCODE = '55000';
            END IF;
        END $$;
        DROP TRIGGER guard_offline_permit_exception_cutoff ON public.offline_license_permits;
        DROP FUNCTION public.guard_offline_permit_exception_cutoff();
        DROP FUNCTION public.exception_outgoing_released(uuid,bigint,timestamptz);
        DROP FUNCTION public.exception_fence_inspect(uuid,uuid,uuid,bigint,text,uuid,uuid,uuid,uuid,uuid,uuid,int,timestamptz);
        DROP TABLE public.merchant_coverage_exception_activations, public.merchant_coverage_exception_cutoffs,
            public.merchant_coverage_exception_decisions, public.coverage_exception_consent_events;
        ALTER TABLE public.entitlement_messages DROP CONSTRAINT entitlement_messages_kind_check,
            ADD CHECK (kind IN ({PreviousKinds}));
        """);
}
