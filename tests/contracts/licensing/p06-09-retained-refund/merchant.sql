-- PostgreSQL 17 local simulator purchase, P06-09 EndAt approval, before signed delivery.
-- Source d131ed868f4f40d8d9f2b5aa340b2ca85bf3417a. Original revision 3 event
-- f8cb5f1e-4c5f-4ffc-a612-f9f66ba58c0b, SHA-256 37bb7c331dbfd8724883e6b05199701d5f59c66ae0190cd51a82c10517fa7927.
-- Restore captured facts without rerunning the original writers. Foreign keys stay active.
BEGIN;
ALTER TABLE public.shops DISABLE TRIGGER USER;
ALTER TABLE public.shop_memberships DISABLE TRIGGER USER;
ALTER TABLE public.shop_affiliation_consent_events DISABLE TRIGGER USER;
ALTER TABLE public.shop_affiliation_delivery_receipts DISABLE TRIGGER USER;
INSERT INTO public.shops (id, name, address, city, state, pincode, contact_person, mobile_number, gst_number, created_at, updated_at, e_invoice_attested_at, e_invoice_attested_by_user_id, e_invoice_status, invoice_series_code, legal_name, state_code, tax_regime, business_time_zone) VALUES ('52489830-3270-4706-9d33-a6fc5a16493f', 'Replay Shop', 'Fixture Road', 'Bengaluru', 'Karnataka', '560001', NULL, NULL, NULL, '2026-11-25 14:11:02.359593+00', NULL, NULL, NULL, 0, 'A', NULL, NULL, 0, 'Asia/Kolkata');
INSERT INTO public.shop_memberships (id, shop_id, user_id, role, is_default, last_used_at, created_at, updated_at, authority_version, status) VALUES ('8732c7c2-a649-450d-9f6d-e5a83df62362', '52489830-3270-4706-9d33-a6fc5a16493f', '21cd4ed2-cbd2-4221-a1aa-181f7c7b9fa4', 'Owner', true, '2026-11-25 14:11:03.462468+00', '2026-11-25 14:11:02.392008+00', '2026-11-25 14:11:03.491388+00', 0, 'Active');
INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id, actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id, shop_name, owner_name, request_expires_at) VALUES ('8fb74dcd-6721-4cf4-b86d-e505ae3d34b7', '52489830-3270-4706-9d33-a6fc5a16493f', '75f50c90-7ca6-8ae7-b5ab-d1c4a839db20', 'ba824ca4-5114-47f5-89e3-d5681f406333', '21cd4ed2-cbd2-4221-a1aa-181f7c7b9fa4', '8732c7c2-a649-450d-9f6d-e5a83df62362', 0, '45b62589-676a-492c-84bb-69411b9f946b', 'Granted', '2026-11-25 14:11:00+00', NULL, 'Replay Shop', 'Billing Fixture', '2026-12-02 14:11:00+00');
INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id, actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id, shop_name, owner_name, request_expires_at) VALUES ('786f0571-5f51-47ad-ad04-c17fb40e3ca7', '52489830-3270-4706-9d33-a6fc5a16493f', '75f50c90-7ca6-8ae7-b5ab-d1c4a839db20', 'ba824ca4-5114-47f5-89e3-d5681f406333', '21cd4ed2-cbd2-4221-a1aa-181f7c7b9fa4', '8732c7c2-a649-450d-9f6d-e5a83df62362', 0, '7230ae46-9dd1-4a5c-beac-cf67ac03e5df', 'Accepted', '2026-11-25 14:11:00+00', NULL, 'Replay Shop', 'Billing Fixture', '2026-12-02 14:11:00+00');
INSERT INTO public.shop_affiliation_delivery_receipts (event_id, shop_id, status) VALUES ('786f0571-5f51-47ad-ad04-c17fb40e3ca7', '52489830-3270-4706-9d33-a6fc5a16493f', 'Applied');
SET CONSTRAINTS ALL IMMEDIATE;
ALTER TABLE public.shops ENABLE TRIGGER USER;
ALTER TABLE public.shop_memberships ENABLE TRIGGER USER;
ALTER TABLE public.shop_affiliation_consent_events ENABLE TRIGGER USER;
ALTER TABLE public.shop_affiliation_delivery_receipts ENABLE TRIGGER USER;
COMMIT;
