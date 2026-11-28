BEGIN;
INSERT INTO users(id, email, password_hash, first_name, last_name, language, is_email_verified, created_at, updated_at)
SELECT id::uuid, address, :'hash', 'Notice', 'Fixture', locale, true, now(), now() FROM (VALUES
    (:'payer','owner@p0702.test','hi-IN'), (:'shop_owner','shop@p0702.test','ta-IN'),
    (:'manager','manager@p0702.test','en-IN'), (:'staff','staff@p0702.test','en-IN'), (:'foreign_user','other@p0702.test','en-IN')) u(id,address,locale);
INSERT INTO shops(id,name,address,city,state,pincode,created_at,updated_at)
VALUES (:'shop','Local notice shop','Fixture address','Bengaluru','Karnataka','560001',now(),now());
INSERT INTO shop_memberships(id,shop_id,user_id,role,is_default,created_at,updated_at)
VALUES (:'membership',:'shop',:'shop_owner','Owner',true,now(),now()),(gen_random_uuid(),:'shop',:'staff','Staff',true,now(),now());
INSERT INTO shop_affiliation_consent_events
    (id,shop_id,account_id,request_id,actor_id,membership_id,authority_version,operation_key,kind,
     recorded_at,term_id,shop_name,owner_name,request_expires_at)
VALUES (gen_random_uuid(),:'shop',:'account',:'affiliation',:'shop_owner',:'membership',0,gen_random_uuid(),
    'Granted',now(),:'term','Local notice shop','Shop owner',now() + interval '1 day');
INSERT INTO shop_affiliation_consent_events
    (id,shop_id,account_id,request_id,actor_id,membership_id,authority_version,operation_key,kind,
     recorded_at,term_id,shop_name,owner_name,request_expires_at)
VALUES (gen_random_uuid(),:'shop',:'account',:'affiliation',:'shop_owner',:'membership',0,gen_random_uuid(),
    'Accepted',now(),:'term','Local notice shop','Shop owner',now() + interval '1 day');
COMMIT;
