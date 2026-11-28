BEGIN;
INSERT INTO billing_accounts(id,name,created_by_merchant_user_id,creation_key,creation_request_hash,created_at)
VALUES (:'account','Local notice account',:'payer',gen_random_uuid(),decode(repeat('12',32),'hex'),now()),
       (:'other_account','Other account',:'foreign_user',gen_random_uuid(),decode(repeat('34',32),'hex'),now());
INSERT INTO billing_account_members(id,account_id,merchant_user_id,role,status,revision,created_at)
VALUES (gen_random_uuid(),:'account',:'payer',1,1,1,now()),(gen_random_uuid(),:'other_account',:'foreign_user',1,1,1,now()),
       (gen_random_uuid(),:'account',:'manager',2,1,1,now());
SELECT date_trunc('second',now()) + interval '7 days 60 seconds' AS term_end \gset
SELECT :'term_end'::timestamptz - interval '1 year' AS term_start \gset
INSERT INTO subscription_coverage_terms(term_id,account_id,mode,capacity,starts_at,ends_at)
VALUES (:'term',:'account','Independent',1,:'term_start',:'term_end');
INSERT INTO subscription_term_facts(term_id,account_id,basis,billing_period,anchor_local,time_zone,period_ordinal,calendar_version,
    access_policy_version,member_allowance,price_paise,currency,source_reference,development_fixture,recorded_at)
VALUES (:'term',:'account','Paid','Year',:'term_start'::timestamptz AT TIME ZONE 'Asia/Kolkata','Asia/Kolkata',0,
    'calendar-month-year/1','access-grace/1',5,149900,'INR','local-fixture-p0702',true,now());
INSERT INTO shop_affiliation_requests(request_id,account_id,account_name,shop_id,requester_id,requester_revision,operation_key,
    term_id,created_at,expires_at)
VALUES (:'affiliation',:'account','Local notice account',:'shop',:'payer',1,gen_random_uuid(),:'term',:'term_start',
    :'term_start'::timestamptz + interval '7 days');
INSERT INTO shop_affiliations(request_id,account_id,shop_id,term_id,event_id,actor_id,membership_id,authority_version,shop_name,
    owner_name,accepted_at,applied_at,coverage_ends_at)
VALUES (:'affiliation',:'account',:'shop',:'term',gen_random_uuid(),:'shop_owner',:'membership',0,'Local notice shop','Shop owner',
    :'term_start',:'term_start',:'term_end');
COMMIT;
