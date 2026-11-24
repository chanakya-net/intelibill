namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class ScheduledCapacityReductions
{
    private const string EffectiveCapacity = """
        coalesce((SELECT i.capacity FROM public.capacity_increases i WHERE i.account_id = t.account_id
            AND i.term_id = t.term_id AND i.state = 'Applied' ORDER BY i.target_revision DESC LIMIT 1), t.capacity)
        """;

    private static readonly string ReductionGuards = $$"""
        CREATE FUNCTION public.guard_reduction_preview_shop() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF EXISTS (SELECT FROM public.capacity_reduction_previews p WHERE p.account_id = NEW.account_id AND p.preview_id = NEW.preview_id)
                OR NOT EXISTS (SELECT FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.shop_id = NEW.shop_id
                        AND a.request_id = NEW.affiliation_request_id AND a.shop_name = NEW.shop_name) THEN
                RAISE EXCEPTION 'Reduction preview shop must be affiliated and inserted before its header' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER reduction_preview_shop_guard BEFORE INSERT ON public.capacity_reduction_preview_shops
            FOR EACH ROW EXECUTE FUNCTION public.guard_reduction_preview_shop();

        CREATE FUNCTION public.require_reduction_preview_seal() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.capacity_reduction_previews p WHERE p.account_id = NEW.account_id
                AND p.preview_id = NEW.preview_id AND p.xmin = pg_current_xact_id()::xid AND p.replay_id = NEW.replay_id) THEN
                RAISE EXCEPTION 'Reduction preview shop and header must be inserted together' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER reduction_preview_shop_sealed AFTER INSERT ON public.capacity_reduction_preview_shops
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_reduction_preview_seal();

        CREATE FUNCTION public.guard_reduction_preview() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE named bigint;
        DECLARE hash text;
        BEGIN
            SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex')
            INTO named, hash FROM public.capacity_reduction_preview_shops s
            WHERE s.account_id = NEW.account_id AND s.preview_id = NEW.preview_id AND s.replay_id = NEW.replay_id;
            IF NEW.actor_id IS DISTINCT FROM {{Actor}} OR named <> NEW.source_shop_count OR hash IS DISTINCT FROM NEW.source_shop_hash
                OR (SELECT count(*) FROM public.capacity_reduction_preview_shops s
                    WHERE s.account_id = NEW.account_id AND s.preview_id = NEW.preview_id) <> named
                OR NOT EXISTS (SELECT FROM public.subscription_coverage_terms t
                    JOIN public.subscription_term_facts f USING (account_id, term_id)
                    WHERE t.account_id = NEW.account_id AND t.term_id = NEW.term_id AND f.basis = 'Paid'
                        AND {{EffectiveCapacity}} = NEW.prior_capacity
                        AND t.starts_at = NEW.current_starts_at AND t.ends_at = NEW.current_paid_through)
                OR EXISTS (SELECT FROM public.payment_fulfilments pf
                    WHERE pf.account_id = NEW.account_id AND pf.predecessor_term_id = NEW.term_id)
                OR (NEW.snapshot->>'accountId')::uuid IS DISTINCT FROM NEW.account_id
                OR (NEW.snapshot->>'previewId')::uuid IS DISTINCT FROM NEW.preview_id
                OR (NEW.snapshot->>'termId')::uuid IS DISTINCT FROM NEW.term_id
                OR (NEW.snapshot->>'amountPaise')::bigint IS DISTINCT FROM NEW.amount_paise
                OR NEW.snapshot->>'snapshotHash' IS DISTINCT FROM NEW.snapshot_hash THEN
                RAISE EXCEPTION 'Reduction preview evidence is incomplete' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER reduction_preview_guard BEFORE INSERT ON public.capacity_reduction_previews
            FOR EACH ROW EXECUTE FUNCTION public.guard_reduction_preview();

        CREATE FUNCTION public.guard_scheduled_reduction_shop() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF EXISTS (SELECT FROM public.scheduled_capacity_reductions s WHERE s.account_id = NEW.account_id AND s.schedule_id = NEW.schedule_id)
                OR NOT EXISTS (SELECT FROM public.shop_affiliations a
                    WHERE a.account_id = NEW.account_id AND a.shop_id = NEW.shop_id
                        AND a.request_id = NEW.affiliation_request_id AND a.shop_name = NEW.shop_name) THEN
                RAISE EXCEPTION 'Scheduled reduction shop must be affiliated and inserted before its schedule' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER scheduled_reduction_shop_guard BEFORE INSERT ON public.scheduled_capacity_reduction_shops
            FOR EACH ROW EXECUTE FUNCTION public.guard_scheduled_reduction_shop();

        CREATE FUNCTION public.require_scheduled_reduction_seal() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM public.scheduled_capacity_reductions s WHERE s.account_id = NEW.account_id
                AND s.schedule_id = NEW.schedule_id AND s.xmin = pg_current_xact_id()::xid AND s.replay_id = NEW.replay_id) THEN
                RAISE EXCEPTION 'Scheduled reduction shop and header must be inserted together' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER scheduled_reduction_shop_sealed AFTER INSERT ON public.scheduled_capacity_reduction_shops
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.require_scheduled_reduction_seal();

        CREATE FUNCTION public.guard_scheduled_reduction() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        DECLARE all_count bigint;
        DECLARE all_hash text;
        DECLARE kept_count bigint;
        DECLARE kept_hash text;
        BEGIN
            IF TG_OP = 'INSERT' THEN
                SELECT count(*), encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id), 'UTF8')), 'hex'),
                    count(*) FILTER (WHERE s.retained),
                    encode(sha256(convert_to(string_agg(s.shop_id::text, ',' ORDER BY s.shop_id) FILTER (WHERE s.retained), 'UTF8')), 'hex')
                INTO all_count, all_hash, kept_count, kept_hash
                FROM public.scheduled_capacity_reduction_shops s
                WHERE s.account_id = NEW.account_id AND s.schedule_id = NEW.schedule_id AND s.replay_id = NEW.replay_id;
                IF NEW.actor_id IS DISTINCT FROM {{Actor}} OR NEW.state <> 'Scheduled'
                    OR all_count <> NEW.source_shop_count OR all_hash IS DISTINCT FROM NEW.source_shop_hash
                    OR kept_count <> NEW.retained_shop_count OR kept_hash IS DISTINCT FROM NEW.retained_shop_hash
                    OR EXISTS (SELECT FROM public.scheduled_capacity_reduction_shops s
                        WHERE s.account_id = NEW.account_id AND s.schedule_id = NEW.schedule_id AND s.retained
                            AND (s.active_members <= 0 OR s.active_members > NEW.target_member_allowance))
                    OR NOT EXISTS (SELECT FROM public.capacity_reduction_previews p
                        WHERE p.account_id = NEW.account_id AND p.preview_id = NEW.preview_id AND p.term_id = NEW.term_id
                            AND p.amount_paise = NEW.amount_paise AND p.target_plan_version_id = NEW.target_plan_version_id
                            AND p.target_capacity = NEW.target_capacity AND p.target_member_allowance = NEW.target_member_allowance
                            AND p.target_mode = NEW.target_mode AND p.source_shop_count = NEW.source_shop_count
                            AND p.source_shop_hash = NEW.source_shop_hash AND p.source_hash = NEW.source_hash
                            AND p.current_paid_through = NEW.effective_at AND p.renewal_ends_at = NEW.renewal_ends_at
                            AND NEW.created_at >= p.created_at AND NEW.created_at < p.expires_at) THEN
                    RAISE EXCEPTION 'Scheduled reduction must match its unexpired preview and resolved counts' USING ERRCODE = '23514';
                END IF;
            ELSE
                IF OLD.state IN ('Applied', 'NeedsReview')
                    OR (to_jsonb(NEW) - 'state' - 'reason' - 'conflicts' - 'resolution_generation' - 'order_id' - 'attempt_id'
                        - 'receipt_id' - 'payment_effective_at' - 'grace_days_included' - 'funded_at' - 'reserve_event'
                        - 'consume_event' - 'reserve_expires_at' - 'successor_term_id' - 'applied_at')
                    IS DISTINCT FROM (to_jsonb(OLD) - 'state' - 'reason' - 'conflicts' - 'resolution_generation' - 'order_id'
                        - 'attempt_id' - 'receipt_id' - 'payment_effective_at' - 'grace_days_included' - 'funded_at'
                        - 'reserve_event' - 'consume_event' - 'reserve_expires_at' - 'successor_term_id' - 'applied_at')
                    OR (OLD.order_id IS NOT NULL AND NEW.order_id IS DISTINCT FROM OLD.order_id)
                    OR (OLD.attempt_id IS NOT NULL AND NEW.attempt_id IS DISTINCT FROM OLD.attempt_id)
                    OR (OLD.receipt_id IS NOT NULL AND NEW.receipt_id IS DISTINCT FROM OLD.receipt_id)
                    OR NEW.resolution_generation < OLD.resolution_generation
                    OR (NEW.state = 'Applied' AND (NEW.consume_event IS NULL OR NEW.successor_term_id IS NULL)) THEN
                    RAISE EXCEPTION 'Scheduled reduction state transition is invalid' USING ERRCODE = '23514';
                END IF;
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER scheduled_reduction_guard BEFORE INSERT OR UPDATE ON public.scheduled_capacity_reductions
            FOR EACH ROW EXECUTE FUNCTION public.guard_scheduled_reduction();
        """;
}
