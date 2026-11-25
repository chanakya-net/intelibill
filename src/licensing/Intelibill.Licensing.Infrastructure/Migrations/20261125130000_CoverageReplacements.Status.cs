namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class CoverageReplacements
{
    private const string ActivationStatus = """
        CREATE FUNCTION public.replacement_activation(id uuid, at_time timestamptz)
        RETURNS text LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r public.coverage_replacements%ROWTYPE;
                old_account text := current_setting('app.billing_account_id',true);
                outcome text;
        BEGIN
            SELECT * INTO r FROM public.coverage_replacements WHERE replacement_id=id;
            IF NOT FOUND OR r.state<>'Applied' THEN RETURN 'Pending'; END IF;
            IF r.revoke_event IS NOT NULL THEN RETURN 'NeedsReview'; END IF;
            IF r.incoming_coverage_from>at_time THEN RETURN 'Waiting'; END IF;
            PERFORM set_config('app.billing_account_id',r.account_id::text,true);
            SELECT CASE
                WHEN o.status IN ('NeedsReview','DeadLetter') THEN 'NeedsReview'
                WHEN h.snap->'shop'->>'state'='Closed' THEN 'Closed'
                WHEN (h.snap->'shop'->'suspension'->>'effectiveAt')::timestamptz<=at_time
                    OR (h.snap->'account'->'suspension'->>'effectiveAt')::timestamptz<=at_time THEN 'Suspended'
                WHEN h.snap->'shop'->>'replacementId'=r.replacement_id::text AND h.snap->'shop'->>'state' IN ('Active','Grace')
                    AND (h.snap->'shop'->>'accessEndsAt')::timestamptz>at_time AND EXISTS(
                        SELECT FROM public.applied_entitlement_shop_receipts a
                        JOIN public.entitlement_merchant_inbox i ON i.event_id=s.event_id AND i.kind='Acknowledgement'
                        WHERE convert_from(i.payload,'UTF8')::jsonb->>'activatedReplacementId'=r.replacement_id::text AND a.account_id=s.account_id
                            AND a.shop_id=s.shop_id AND a.affiliation_request_id=s.affiliation_request_id AND a.shop_revision=s.shop_revision)
                    THEN 'Acknowledged'
                WHEN h.snap->'shop'->>'state' IN ('Expired','Restricted') THEN 'TermEnded'
                ELSE 'Applying' END INTO outcome
            FROM public.entitlement_snapshots s JOIN public.entitlement_messages m USING(event_id)
            JOIN public.entitlement_outbox o USING(event_id)
            CROSS JOIN LATERAL (SELECT convert_from(m.payload,'UTF8')::jsonb AS snap) h
            WHERE s.account_id=r.account_id AND s.shop_id=r.incoming_shop_id AND s.affiliation_request_id=r.incoming_affiliation_id
            ORDER BY s.shop_revision DESC LIMIT 1;
            PERFORM set_config('app.billing_account_id',coalesce(old_account,''),true);
            RETURN coalesce(outcome,'Applying');
        END $$;
        """;
}
