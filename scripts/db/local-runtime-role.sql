-- The restricted runtime role for the LOCAL Docker stack (docker-compose.yml).
--
-- Deployed environments get this from scripts/db/phase7-bootstrap.sql, where the
-- identities are managed and the names come from Terraform output. This file is
-- the same shape for one local database, with a fixed name and a password that
-- is no more secret than the one already in docker-compose.yml.
--
-- Why it exists: the compose Postgres superuser `intelibill` owns the schema and
-- holds BYPASSRLS, so an API connecting as that role ignores every tenant policy
-- without a single failure in the log. RuntimeRoleGuard refuses to start on such
-- an identity, which is what the backend container was hitting.
--
-- Run by the `db-runtime-role` compose service before the backend starts. It is
-- idempotent: safe on a fresh volume and on one that already has the role.
--
--   psql --host=localhost --username=intelibill --dbname=intelibill_dev \
--     --file=scripts/db/local-runtime-role.sql

\set ON_ERROR_STOP on
\timing off

-- Create once; re-state the attributes every time. A role left over from an
-- experiment that granted it BYPASSRLS would silently disarm every policy in the
-- database, and nothing downstream would report it.
do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'intelibill_app') then
    create role intelibill_app login password 'intelibill_app_password';
  end if;
end
$$;

alter role intelibill_app with login password 'intelibill_app_password'
  nosuperuser nocreatedb nocreaterole nobypassrls noinherit;

grant connect on database intelibill_dev to intelibill_app;

-- USAGE without CREATE, and no ownership: a table's owner may drop its own
-- policy, so the runtime identity must never own one.
revoke create on schema public from public;
revoke create on schema public from intelibill_app;
grant usage on schema public to intelibill_app;

-- Default privileges cover what the migrator creates NEXT. The migrator here is
-- `intelibill`, the same role that runs `dotnet ef database update`, so a table
-- added by a later migration is reachable without re-running this file.
alter default privileges in schema public
  grant select, insert, update, delete on tables to intelibill_app;
alter default privileges in schema public
  grant usage, select on sequences to intelibill_app;

-- And everything that already exists, which default privileges cannot reach.
grant select, insert, update, delete on all tables in schema public to intelibill_app;
grant usage, select on all sequences in schema public to intelibill_app;

\echo ''
\echo '== the runtime role must not be able to ignore a policy: expect zero rows =='
select rolname, rolsuper, rolbypassrls, rolcreatedb, rolcreaterole
from pg_roles
where rolname = 'intelibill_app'
  and (rolsuper or rolbypassrls or rolcreatedb or rolcreaterole);

\echo ''
\echo '== schema rights and ownership: expect usage_t=t, create_f=f, owned_tables=0 =='
select has_schema_privilege('intelibill_app', 'public', 'USAGE')  as usage_t,
       has_schema_privilege('intelibill_app', 'public', 'CREATE') as create_f,
       (select count(*) from pg_tables
        where schemaname = 'public' and tableowner = 'intelibill_app') as owned_tables;

\echo ''
\echo '== tables the runtime role cannot read or write: expect zero rows =='
select c.relname as table_name
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'public'
  and c.relkind = 'r'
  and c.relname <> '__EFMigrationsHistory'
  and not (has_table_privilege('intelibill_app', c.oid, 'SELECT')
       and has_table_privilege('intelibill_app', c.oid, 'INSERT')
       and has_table_privilege('intelibill_app', c.oid, 'UPDATE')
       and has_table_privilege('intelibill_app', c.oid, 'DELETE'))
order by c.relname;
