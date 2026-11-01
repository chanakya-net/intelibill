-- Tenant isolation evidence for one database.
--
-- Run it as part of a deployment, after the migration job and before traffic is
-- shifted, and keep the output with the release artifact. Every query below is
-- written so that EMPTY OUTPUT IS THE PASS: a row is a finding.
--
--   psql "host=<server> user=<migrator> dbname=<database> sslmode=require" \
--     -v app_role=id-app-prod \
--     -f scripts/db/verify-tenant-policies.sql
--
-- The authority on how each table should be treated is
-- src/backend/Intelibill.Infrastructure/Data/TenantPolicyManifest.cs, which the
-- test suite compares against the EF model for exact set equality.
-- docs/security/tenant-policy-matrix.md is the readable form of the same thing.

\pset pager off
\timing off

\echo ''
\echo '=== 1. Tenant tables without ENABLE and FORCE row level security ========'
\echo '--- Any table with a shop_id column, other than the two shop-identity'
\echo '--- tables that are exempt by decision. Expect zero rows.'
SELECT c.relname AS table_name, c.relrowsecurity AS enabled, c.relforcerowsecurity AS forced
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
\echo '=== 2. Policies that do not state both clauses, or mis-parse an absent ==='
\echo '=== shop context ========================================================'
\echo '--- USING without WITH CHECK leaves the write side to a default, and a'
\echo '--- predicate without NULLIF raises 22P02 when no shop is active instead'
\echo '--- of matching nothing. Expect zero rows.'
SELECT c.relname AS table_name,
       p.polname AS policy_name,
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
\echo '=== 3. Identity tables that have been brought under row level security =='
\echo '--- These are read and written with no usable shop context by login,'
\echo '--- token validation, shop switching and invitation acceptance. A policy'
\echo '--- on any of them breaks those flows. Expect zero rows.'
SELECT c.relname AS table_name,
       c.relrowsecurity AS enabled,
       (SELECT count(*) FROM pg_policy p WHERE p.polrelid = c.oid) AS policies
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relname IN ('users', 'shops', 'shop_memberships', 'shop_invitations', 'auth_sessions',
                    'refresh_tokens', 'password_reset_tokens', 'phone_challenges',
                    'email_ownership_challenges', 'identity_mail', 'identity_sms',
                    'rate_limit_buckets')
  AND (c.relrowsecurity OR EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid))
ORDER BY c.relname;

\echo ''
\echo '=== 4. Policied tables whose predicate column does not lead an index ===='
\echo '--- The predicate runs on every row the statement touches. Expect zero'
\echo '--- rows.'
WITH policied AS (
  SELECT c.oid,
         c.relname,
         CASE c.relname
           WHEN 'purchase_order_lines'         THEN 'purchase_order_id'
           WHEN 'purchase_order_receipt_lines' THEN 'purchase_order_receipt_id'
           ELSE 'shop_id'
         END AS predicate_column
  FROM pg_class c
  JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname = 'public' AND c.relkind = 'r' AND c.relrowsecurity
)
SELECT p.relname AS table_name, p.predicate_column
FROM policied p
WHERE NOT EXISTS (
  SELECT 1
  FROM pg_index i
  JOIN pg_attribute a ON a.attrelid = p.oid AND a.attnum = i.indkey[0]
  WHERE i.indrelid = p.oid AND a.attname = p.predicate_column)
ORDER BY p.relname;

\echo ''
\echo '=== 5. The runtime identity ============================================='
\echo '--- A superuser, or a role holding BYPASSRLS, ignores every policy above'
\echo '--- and nothing in the logs says so. RuntimeRoleGuard refuses to start on'
\echo '--- a row here. Expect zero rows. Never resolve this by granting'
\echo '--- BYPASSRLS.'
SELECT rolname, rolsuper, rolbypassrls, rolcreatedb, rolcreaterole
FROM pg_roles
WHERE rolname = :'app_role'
  AND (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole);

\echo ''
\echo '=== 6. Runtime identity grants =========================================='
\echo '--- USAGE on the schema without CREATE, and it must own no table: a table'
\echo '--- owner can drop its own policy. Expect usage_t=t, create_f=f,'
\echo '--- owned_tables=0.'
SELECT has_schema_privilege(:'app_role', 'public', 'USAGE')  AS usage_t,
       has_schema_privilege(:'app_role', 'public', 'CREATE') AS create_f,
       (SELECT count(*) FROM pg_tables
        WHERE schemaname = 'public' AND tableowner = :'app_role') AS owned_tables;

\echo ''
\echo '=== 7. Tables the runtime identity cannot read or write ================='
\echo '--- Default privileges only cover what the migrator creates next, so a'
\echo '--- table created before the grants fails late and on that table only.'
\echo '--- Expect zero rows.'
SELECT c.relname AS table_name
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relkind = 'r'
  AND c.relname <> '__EFMigrationsHistory'
  AND NOT (has_table_privilege(:'app_role', c.oid, 'SELECT')
       AND has_table_privilege(:'app_role', c.oid, 'INSERT')
       AND has_table_privilege(:'app_role', c.oid, 'UPDATE')
       AND has_table_privilege(:'app_role', c.oid, 'DELETE'))
ORDER BY c.relname;

\echo ''
\echo '=== 8. Pooling =========================================================='
\echo '--- Not answerable from the catalog: the shop context is a session'
\echo '--- variable on a connection the pool hands round, so it is proved by'
\echo '--- PooledConnectionTenantContextTests (shop A, shop B and no shop over a'
\echo '--- single pinned connection, including synchronous opening, an explicit'
\echo '--- transaction, a cancelled operation and traffic from connections that'
\echo '--- never pass through the EF interceptor). Record that run with this'
\echo '--- output.'
