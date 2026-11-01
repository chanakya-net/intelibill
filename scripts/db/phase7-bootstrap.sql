-- Phase 7 database bootstrap — one shared server, one database per environment.
-- See docs/infrastructure-implementation-guide.md Phase 7.
--
-- Run against dbname=postgres on intelibill-pg-01 as the Entra administrator.
-- Principal (object) IDs are passed in as psql variables, never hardcoded:
--
--   psql "host=intelibill-pg-01.postgres.database.azure.com \
--         user=<your-upn> dbname=postgres sslmode=require" \
--     -v app_dev_oid="$(tofu -chdir=.tofu/envs/dev  output -json identities | jq -r .app.principal_id)" \
--     -v mig_dev_oid="$(tofu -chdir=.tofu/envs/dev  output -json identities | jq -r .migrator.principal_id)" \
--     -v app_prod_oid="$(tofu -chdir=.tofu/envs/prod output -json identities | jq -r .app.principal_id)" \
--     -v mig_prod_oid="$(tofu -chdir=.tofu/envs/prod output -json identities | jq -r .migrator.principal_id)" \
--     -f scripts/db/phase7-bootstrap.sql
--
-- IMPORTANT: pass the *principal* (object) ID, not the client ID. They are both
-- GUIDs on the same identity and using the wrong one produces a principal that
-- can never authenticate, with no error at creation time.

\set ON_ERROR_STOP on
\timing off

-- ---------------------------------------------------------------------------
-- 7.3 Server principals for the managed identities
--
-- The principal NAME must exactly match the managed identity's name — it is
-- what the identity presents when authenticating.
--
-- The trailing `false, false` are isAdmin and isMfa. isAdmin MUST stay false:
-- on a shared server an admin principal reaches both databases regardless of
-- the CONNECT grants below, which would defeat the entire isolation model.
--
-- Error tolerance is relaxed here only because re-running this script should
-- not fail on principals that already exist. It is restored immediately after.
-- ---------------------------------------------------------------------------
\set ON_ERROR_STOP off

SELECT * FROM pgaadauth_create_principal_with_oid('id-app-dev',       :'app_dev_oid',  'service', false, false);
SELECT * FROM pgaadauth_create_principal_with_oid('id-migrator-dev',  :'mig_dev_oid',  'service', false, false);
SELECT * FROM pgaadauth_create_principal_with_oid('id-app-prod',      :'app_prod_oid', 'service', false, false);
SELECT * FROM pgaadauth_create_principal_with_oid('id-migrator-prod', :'mig_prod_oid', 'service', false, false);

\set ON_ERROR_STOP on

-- Confirm all four exist before granting anything to them.
SELECT rolname FROM pg_roles WHERE rolname LIKE 'id-%' ORDER BY rolname;

-- ---------------------------------------------------------------------------
-- 7.4 The isolation grants
--
-- On a single shared server this is THE boundary between dev and production,
-- not defence in depth. PostgreSQL grants CONNECT to PUBLIC on every new
-- database by default, so a database left un-revoked is open to every
-- principal on the server — silently, with nothing in the logs.
-- ---------------------------------------------------------------------------
REVOKE CONNECT ON DATABASE intelibill_dev  FROM PUBLIC;
REVOKE CONNECT ON DATABASE intelibill_prod FROM PUBLIC;

-- CONNECT is not the only default grant to PUBLIC: TEMPORARY is too, and
-- revoking CONNECT alone leaves `=T/azure_pg_admin` in datacl. It is not
-- exploitable without CONNECT, but any role later granted CONNECT would inherit
-- temp-table rights it was never meant to have.
REVOKE TEMPORARY ON DATABASE intelibill_dev  FROM PUBLIC;
REVOKE TEMPORARY ON DATABASE intelibill_prod FROM PUBLIC;

GRANT CONNECT ON DATABASE intelibill_dev  TO "id-app-dev",  "id-migrator-dev";
GRANT CONNECT ON DATABASE intelibill_prod TO "id-app-prod", "id-migrator-prod";

-- Explicit cross-environment denial. Redundant if the REVOKE above worked, and
-- exactly what you want if it silently did not.
REVOKE ALL ON DATABASE intelibill_prod FROM "id-app-dev",  "id-migrator-dev";
REVOKE ALL ON DATABASE intelibill_dev  FROM "id-app-prod", "id-migrator-prod";

-- --- dev database ----------------------------------------------------------
\c intelibill_dev

REVOKE ALL ON SCHEMA public FROM PUBLIC;

GRANT USAGE          ON SCHEMA public TO "id-app-dev";
GRANT CREATE, USAGE  ON SCHEMA public TO "id-migrator-dev";

-- The migrator owns what it creates. Without default privileges the runtime
-- identity has no access to anything a later migration adds — and it fails
-- late: everything works until the next migration, then permission denied on
-- that table only.
ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-dev" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "id-app-dev";
ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-dev" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO "id-app-dev";

-- Default privileges only cover what the migrator creates *next*. Tables that
-- already exist — everything up to the migration that ran before this script —
-- need the grant stated outright, or the runtime identity is refused on exactly
-- the tables nobody thought to re-check.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES    IN SCHEMA public TO "id-app-dev";
GRANT USAGE, SELECT                 ON ALL SEQUENCES  IN SCHEMA public TO "id-app-dev";

-- The runtime identity must not be able to turn the tenant boundary off. It
-- owns no table, so ALTER TABLE ... DISABLE ROW LEVEL SECURITY is refused for
-- it; this states the intent so a future ownership change is a visible break.
REVOKE CREATE ON SCHEMA public FROM "id-app-dev";

-- --- prod database ---------------------------------------------------------
\c intelibill_prod

REVOKE ALL ON SCHEMA public FROM PUBLIC;

GRANT USAGE          ON SCHEMA public TO "id-app-prod";
GRANT CREATE, USAGE  ON SCHEMA public TO "id-migrator-prod";

ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-prod" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "id-app-prod";
ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-prod" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO "id-app-prod";

-- Default privileges only cover what the migrator creates *next*. Tables that
-- already exist — everything up to the migration that ran before this script —
-- need the grant stated outright, or the runtime identity is refused on exactly
-- the tables nobody thought to re-check.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES    IN SCHEMA public TO "id-app-prod";
GRANT USAGE, SELECT                 ON ALL SEQUENCES  IN SCHEMA public TO "id-app-prod";

-- The runtime identity must not be able to turn the tenant boundary off. It
-- owns no table, so ALTER TABLE ... DISABLE ROW LEVEL SECURITY is refused for
-- it; this states the intent so a future ownership change is a visible break.
REVOKE CREATE ON SCHEMA public FROM "id-app-prod";

-- ---------------------------------------------------------------------------
-- 7.5 Verify isolation — the part that must not be skipped
-- ---------------------------------------------------------------------------
\c postgres

\echo ''
\echo '== datacl: neither database may show =Tc/ for PUBLIC (no role name before =) =='
SELECT datname, datacl FROM pg_database
WHERE datname IN ('intelibill_dev', 'intelibill_prod');

\echo ''
\echo '== CONNECT matrix: expect t, t, f, f — the two f results ARE the boundary =='
SELECT
  has_database_privilege('id-app-dev',  'intelibill_dev',  'CONNECT') AS dev_to_dev,
  has_database_privilege('id-app-prod', 'intelibill_prod', 'CONNECT') AS prod_to_prod,
  has_database_privilege('id-app-dev',  'intelibill_prod', 'CONNECT') AS dev_to_PROD,
  has_database_privilege('id-app-prod', 'intelibill_dev',  'CONNECT') AS prod_to_dev;

\echo ''
\echo '== same for the migrators, which hold CREATE — a crossed grant here is worse =='
SELECT
  has_database_privilege('id-migrator-dev',  'intelibill_dev',  'CONNECT') AS dev_to_dev,
  has_database_privilege('id-migrator-prod', 'intelibill_prod', 'CONNECT') AS prod_to_prod,
  has_database_privilege('id-migrator-dev',  'intelibill_prod', 'CONNECT') AS dev_to_PROD,
  has_database_privilege('id-migrator-prod', 'intelibill_dev',  'CONNECT') AS prod_to_dev;

\echo ''
\echo '== no principal may be a server admin: expect zero rows =='
SELECT rolname, rolsuper, rolcreatedb, rolcreaterole
FROM pg_roles
WHERE rolname LIKE 'id-%' AND (rolsuper OR rolcreatedb OR rolcreaterole);

\echo ''
\echo '== schema privileges: runtime USAGE without CREATE; migrator CREATE =='
\c intelibill_dev
SELECT
  has_schema_privilege('id-app-dev','public','USAGE')       AS app_usage_t,
  has_schema_privilege('id-app-dev','public','CREATE')      AS app_create_f,
  has_schema_privilege('id-migrator-dev','public','CREATE') AS mig_create_t,
  has_schema_privilege('id-app-prod','public','USAGE')      AS crossenv_usage_f;

\c intelibill_prod
SELECT
  has_schema_privilege('id-app-prod','public','USAGE')       AS app_usage_t,
  has_schema_privilege('id-app-prod','public','CREATE')      AS app_create_f,
  has_schema_privilege('id-migrator-prod','public','CREATE') AS mig_create_t;

\echo ''
\echo '== every tenant table must ENABLE and FORCE row level security =='
\echo '== expect zero rows: a row here is a table with no boundary =='
SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relkind = 'r'
  AND (EXISTS (
        SELECT 1 FROM pg_attribute a
        WHERE a.attrelid = c.oid AND a.attname = 'shop_id' AND a.attnum > 0 AND NOT a.attisdropped)
       -- The two line tables derive their shop from a parent and carry no
       -- shop_id of their own, so a column test cannot see them. They are the
       -- only tables whose protection is entirely indirect, which makes them
       -- the ones most worth naming.
       OR c.relname IN ('purchase_order_lines', 'purchase_order_receipt_lines'))
  AND c.relname NOT IN ('shop_memberships', 'shop_invitations')
  AND NOT (c.relrowsecurity AND c.relforcerowsecurity)
ORDER BY c.relname;

\echo ''
\echo '== every tenant policy must state USING *and* WITH CHECK, and parse an =='
\echo '== absent shop context safely. Expect zero rows. =='
SELECT c.relname, p.polname,
       pg_get_expr(p.polqual, p.polrelid)      AS using_expr,
       pg_get_expr(p.polwithcheck, p.polrelid) AS check_expr
FROM pg_policy p
JOIN pg_class c ON c.oid = p.polrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND (p.polqual IS NULL
       OR p.polwithcheck IS NULL
       OR pg_get_expr(p.polqual, p.polrelid)      NOT ILIKE '%nullif%'
       OR pg_get_expr(p.polwithcheck, p.polrelid) NOT ILIKE '%nullif%')
ORDER BY c.relname;

\echo ''
\echo '== the identity tables must stay OUTSIDE row level security: a policy =='
\echo '== on any of them breaks login, shop switching and invitations. =='
\echo '== Expect zero rows. =='
SELECT c.relname
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relname IN ('users', 'shops', 'shop_memberships', 'shop_invitations', 'auth_sessions',
                    'refresh_tokens', 'password_reset_tokens', 'phone_challenges',
                    'email_ownership_challenges', 'identity_mail', 'identity_sms')
  AND (c.relrowsecurity OR EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid))
ORDER BY c.relname;

\echo ''
\echo '== the runtime identity must be subject to the policies it is bound by =='
\echo '== expect zero rows; RuntimeRoleGuard refuses to start on a row here =='
SELECT rolname, rolsuper, rolbypassrls
FROM pg_roles
WHERE rolname IN ('id-app-dev', 'id-app-prod')
  AND (rolsuper OR rolbypassrls);

-- ---------------------------------------------------------------------------
-- STILL TO DO, deliberately not scripted here:
--
-- The distributed-cache table (`CreateIfNotExists=false` in production config)
-- must be created by the migrator before the runtime starts. Its schema comes
-- from the caching provider, so it belongs in a reviewed EF migration rather
-- than invented in this file — see Phase 8.
-- ---------------------------------------------------------------------------

-- ---------------------------------------------------------------------------
-- NO LONGER OUTSTANDING: the runtime privilege checks that used to be listed
-- above as still to do are now executed rather than described.
--
-- RestrictedRoleRlsTests proves the application identity is no superuser, holds
-- no BYPASSRLS, owns no tenant table and cannot disable row-level security.
-- PooledConnectionTenantContextTests proves the shop context is set per request
-- and is never carried from one request to the next by a reused connection.
-- RuntimeRoleGuard repeats the role half of that check at startup in every
-- environment, and refuses to serve traffic when it fails.
--
-- See docs/security/tenant-policy-matrix.md.
-- ---------------------------------------------------------------------------
