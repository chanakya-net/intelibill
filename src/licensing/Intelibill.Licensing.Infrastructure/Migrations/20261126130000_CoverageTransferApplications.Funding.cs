namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageTransferApplications
{
    private const string Funding = """
        ALTER TABLE public.coverage_transfer_proposals ADD COLUMN funding_digest text
            CHECK (length(funding_digest)=64 AND funding_digest !~ '[^a-f0-9]');
        CREATE FUNCTION public.transfer_funding_digest(ac uuid, term uuid, shop uuid, affiliation uuid)
        RETURNS text LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT encode(sha256(convert_to(jsonb_build_array(r.slot_origin_shop_id,r.handoff_generation,r.slot_assignment_id,
                r.interval_starts_at,r.interval_ends_at,coalesce(i.target_revision,0),coalesce(i.members,f.member_allowance))::text,
                'UTF8')),'hex')
            FROM public.term_paid_slot_intervals(ac,term) r JOIN public.subscription_term_facts f
                ON f.account_id=ac AND f.term_id=term AND f.basis='Paid'
            LEFT JOIN LATERAL (SELECT target_revision,members FROM public.capacity_increases
                WHERE account_id=ac AND term_id=term AND state='Applied' ORDER BY target_revision DESC LIMIT 1) i ON true
            WHERE r.shop_id=shop AND r.affiliation_request_id=affiliation AND r.is_final_holder
        $$;
        ALTER FUNCTION public.coverage_transfer_blocker(uuid,timestamptz,timestamptz,text) RENAME TO original_transfer_blocker;
        CREATE FUNCTION public.coverage_transfer_blocker(id uuid, at_time timestamptz,
            merchant_at timestamptz DEFAULT NULL, merchant_digest text DEFAULT NULL)
        RETURNS text LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE previous text:=current_setting('app.billing_account_id',true); p public.coverage_transfer_proposals%ROWTYPE;
            result text:=public.original_transfer_blocker(id,at_time,merchant_at,merchant_digest);
        BEGIN
            IF result IS NOT NULL THEN RETURN result; END IF;
            SELECT * INTO STRICT p FROM public.coverage_transfer_proposals WHERE transfer_id=id;
            PERFORM set_config('app.billing_account_id',p.source_account_id::text,true);
            SELECT CASE
                WHEN p.funding_digest IS NOT NULL AND p.funding_digest IS DISTINCT FROM public.transfer_funding_digest(
                    p.source_account_id,p.source_term_id,p.shop_id,p.source_affiliation_id) THEN 'FundingChanged'
                WHEN p.funding_digest IS NULL AND (EXISTS(SELECT FROM public.capacity_increases
                    WHERE account_id=p.source_account_id AND term_id=p.source_term_id AND state='Applied')
                    OR EXISTS(SELECT FROM public.paid_slot_handoffs WHERE account_id=p.source_account_id AND term_id=p.source_term_id
                        AND p.shop_id IN (outgoing_shop_id,incoming_shop_id))) THEN 'AllocationRequiresNewConsent'
                WHEN EXISTS(SELECT FROM public.capacity_increases WHERE account_id=p.source_account_id AND term_id=p.source_term_id
                    AND state='Pending') OR EXISTS(SELECT FROM public.scheduled_capacity_reductions WHERE account_id=p.source_account_id
                    AND term_id=p.source_term_id AND state IN ('Scheduled','Funded','Applying','NeedsResolution','NeedsReview'))
                    OR EXISTS(SELECT FROM public.coverage_replacements WHERE account_id=p.source_account_id AND term_id=p.source_term_id
                        AND outgoing_shop_id=p.shop_id AND state IN ('AwaitingConsent','Scheduled'))
                    OR EXISTS(SELECT FROM public.coverage_replacement_exceptions WHERE account_id=p.source_account_id AND term_id=p.source_term_id
                        AND outgoing_shop_id=p.shop_id AND state IN ('AwaitingConsents','ReadyForReview','Preparing','Consuming','Aborting'))
                    THEN 'CompetingCoverageChange'
            END INTO result;
            PERFORM set_config('app.billing_account_id',coalesce(previous,''),true);
            RETURN result;
        END $$;
        """;

    private const string UndoFunding = """
        DROP FUNCTION public.coverage_transfer_blocker(uuid,timestamptz,timestamptz,text);
        ALTER FUNCTION public.original_transfer_blocker(uuid,timestamptz,timestamptz,text) RENAME TO coverage_transfer_blocker;
        DROP FUNCTION public.transfer_funding_digest(uuid,uuid,uuid,uuid);
        ALTER TABLE public.coverage_transfer_proposals DROP COLUMN funding_digest;
        """;
}
