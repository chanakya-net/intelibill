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

-- --- prod database ---------------------------------------------------------
\c intelibill_prod

REVOKE ALL ON SCHEMA public FROM PUBLIC;

GRANT USAGE          ON SCHEMA public TO "id-app-prod";
GRANT CREATE, USAGE  ON SCHEMA public TO "id-migrator-prod";

ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-prod" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "id-app-prod";
ALTER DEFAULT PRIVILEGES FOR ROLE "id-migrator-prod" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO "id-app-prod";

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

-- ---------------------------------------------------------------------------
-- STILL TO DO, deliberately not scripted here:
--
-- The distributed-cache table (`CreateIfNotExists=false` in production config)
-- must be created by the migrator before the runtime starts. Its schema comes
-- from the caching provider, so it belongs in a reviewed EF migration rather
-- than invented in this file — see Phase 8.
--
-- Runtime privilege checks that need a real connection as the app identity:
-- confirm it cannot CREATE, ALTER, DROP, or disable RLS, and that RLS still
-- applies under connection pooling (active_shop_id must be set per request,
-- not per connection).
-- ---------------------------------------------------------------------------
