-- Local database preparation for the combined Docker stack (docker-compose.yml).
--
-- LOCAL DEVELOPMENT AND TESTING ONLY. Deployed environments provision their
-- databases and identities through OpenTofu and scripts/db/phase7-bootstrap.sql;
-- the passwords below are fixed, public and no more secret than the ones
-- already in docker-compose.yml.
--
-- Two stages, chosen with -v stage=...; both require -v environment=Development
-- (or Testing) and refuse to do anything otherwise.
--
--   bootstrap  connected to the maintenance database as the compose superuser.
--              Creates the licensing migrator (database owner) and runtime
--              logins and the licensing database, once; re-states the role
--              attributes every time; and confines CONNECT on both service
--              databases to their own identities, before either API or
--              migrator starts. Run by the `db-bootstrap` service.
--
--   grants     connected to the licensing database after its migration job.
--              Grants the runtime login data access to what the migrator has
--              created, and default privileges FOR ROLE the migrator for what
--              it creates next, then refuses to finish if the runtime login can
--              change schema, own anything or assume a privileged role. Run by
--              the `licensing-runtime-role` service.
--
-- Idempotent and serialized by an advisory lock: safe on a fresh volume, on an
-- existing merchant-only volume, and when run twice at once. It never drops,
-- recreates or reassigns an existing object; anything unexpected stops it.
--
--   psql -X --host=localhost --username=intelibill --dbname=postgres \
--     -v environment=Development -v stage=bootstrap \
--     --file=scripts/db/local-licensing-role.sql

\set ON_ERROR_STOP on
\timing off

\if :{?environment}
\else
  \set environment ''
\endif
\if :{?stage}
\else
  \set stage ''
\endif

select :'environment' in ('Development', 'Testing') as p0105_local_environment,
       :'stage' = 'bootstrap' as p0105_bootstrap,
       :'stage' = 'grants' as p0105_grants
\gset

\if :p0105_local_environment
\else
  do $$ begin
    raise exception 'local-licensing-role.sql prepares local Development/Testing stacks only; pass -v environment=Development';
  end $$;
\endif

-- One preparation at a time, across both stages and every concurrent `up`.
select pg_advisory_lock(hashtext('intelibill.local-database-preparation')) is null as p0105_locked \gset

\if :p0105_bootstrap

do $$
begin
  if not exists (select from pg_roles where rolname = 'intelibill_licensing_migrator') then
    create role intelibill_licensing_migrator login password 'intelibill_licensing_migrator_password';
  end if;
  if not exists (select from pg_roles where rolname = 'intelibill_licensing_app') then
    create role intelibill_licensing_app login password 'intelibill_licensing_app_password';
  end if;
end
$$;

-- Re-stated every run: a role left over from an experiment with any of these
-- would silently disarm the isolation below.
alter role intelibill_licensing_migrator with login password 'intelibill_licensing_migrator_password'
  nosuperuser nocreatedb nocreaterole nobypassrls noreplication inherit;
alter role intelibill_licensing_app with login password 'intelibill_licensing_app_password'
  nosuperuser nocreatedb nocreaterole nobypassrls noreplication noinherit;

-- Neither licensing identity may belong to another role, and nobody may be
-- able to assume them except the superuser. Refuse rather than repair.
do $$
begin
  if exists (
    select from pg_auth_members m
    join pg_roles member on member.oid = m.member
    join pg_roles granted on granted.oid = m.roleid
    where member.rolname in ('intelibill_licensing_migrator', 'intelibill_licensing_app', 'intelibill_app')
       or granted.rolname in ('intelibill_licensing_migrator', 'intelibill_licensing_app', 'intelibill_app')
  ) then
    raise exception 'unexpected role membership on a local service identity; inspect pg_auth_members';
  end if;
end
$$;

-- CREATE DATABASE cannot run inside a transaction block, so it goes out
-- through \gexec only when the database is absent.
select format('create database %I owner %I', 'intelibill_licensing_dev', 'intelibill_licensing_migrator')
where not exists (select from pg_database where datname = 'intelibill_licensing_dev')
\gexec

do $$
begin
  if (select pg_get_userbyid(datdba) from pg_database where datname = 'intelibill_licensing_dev')
       is distinct from 'intelibill_licensing_migrator' then
    raise exception 'intelibill_licensing_dev exists with an unexpected owner';
  end if;
  if not exists (select from pg_database where datname = 'intelibill_dev') then
    raise exception 'merchant database intelibill_dev is missing';
  end if;
end
$$;

-- Each service database accepts only its own identities (and the superuser).
-- PUBLIC holds CONNECT and TEMPORARY on every new database by default.
revoke connect, temporary on database intelibill_licensing_dev from public;
revoke connect, temporary on database intelibill_dev from public;
grant connect on database intelibill_licensing_dev to intelibill_licensing_migrator, intelibill_licensing_app;
revoke all on database intelibill_dev from intelibill_licensing_migrator, intelibill_licensing_app;
do $$
begin
  if exists (select from pg_roles where rolname = 'intelibill_app') then
    revoke all on database intelibill_licensing_dev from intelibill_app;
  end if;
end
$$;

\echo '== licensing database prepared: owner, runtime and cross-database access =='
select d.datname,
       pg_get_userbyid(d.datdba) as owner,
       has_database_privilege('intelibill_licensing_app', d.oid, 'CONNECT') as licensing_app_connect,
       has_database_privilege('intelibill_licensing_app', d.oid, 'TEMPORARY') as licensing_app_temp
from pg_database d
where d.datname in ('intelibill_dev', 'intelibill_licensing_dev')
order by d.datname;

\elif :p0105_grants

do $$
begin
  if current_database() <> 'intelibill_licensing_dev' then
    raise exception 'grants stage must run in intelibill_licensing_dev, not %', current_database();
  end if;
  if (select pg_get_userbyid(datdba) from pg_database where datname = current_database())
       <> 'intelibill_licensing_migrator' then
    raise exception 'intelibill_licensing_dev has an unexpected owner';
  end if;
end
$$;

-- USAGE without CREATE: the schema belongs to the migrator (pg_database_owner).
revoke create on schema public from public;
revoke all on schema public from intelibill_licensing_app;
grant usage on schema public to intelibill_licensing_app;

-- What the migrator creates next. FOR ROLE names the identity that actually
-- runs the migration job; default privileges of any other role never apply.
alter default privileges for role intelibill_licensing_migrator in schema public
  grant select, insert, update, delete on tables to intelibill_licensing_app;
alter default privileges for role intelibill_licensing_migrator in schema public
  grant usage, select on sequences to intelibill_licensing_app;

-- And everything that already exists.
grant select, insert, update, delete on all tables in schema public to intelibill_licensing_app;
grant usage, select on all sequences in schema public to intelibill_licensing_app;

-- Migration history is the migrator's record; the runtime may read it only.
do $$
begin
  if to_regclass('public."__EFMigrationsHistory"') is not null then
    revoke insert, update, delete, truncate on public."__EFMigrationsHistory" from intelibill_licensing_app;
  end if;
end
$$;

-- Fail closed: the API would refuse readiness anyway, but a job that exits
-- nonzero keeps the API from starting at all.
do $$
declare
  runtime oid := (select oid from pg_roles where rolname = 'intelibill_licensing_app');
begin
  if exists (select from pg_roles where oid = runtime
             and (rolsuper or rolbypassrls or rolcreatedb or rolcreaterole or rolreplication or rolinherit)) then
    raise exception 'intelibill_licensing_app holds a privileged attribute';
  end if;
  if has_database_privilege(runtime, current_database(), 'CREATE')
     or has_database_privilege(runtime, current_database(), 'TEMPORARY')
     or exists (select from pg_namespace n where has_schema_privilege(runtime, n.oid, 'CREATE')) then
    raise exception 'intelibill_licensing_app can change schema';
  end if;
  if exists (select from pg_auth_members where member = runtime or roleid = runtime) then
    raise exception 'intelibill_licensing_app has a role membership';
  end if;
  if exists (select from pg_shdepend where refobjid = runtime and deptype = 'o') then
    raise exception 'intelibill_licensing_app owns an object';
  end if;
end
$$;

\echo '== licensing runtime grants: tables the runtime role cannot use (expect zero rows) =='
select c.relname as table_name
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'public' and c.relkind = 'r' and c.relname <> '__EFMigrationsHistory'
  and not (has_table_privilege('intelibill_licensing_app', c.oid, 'SELECT, INSERT, UPDATE, DELETE'))
order by c.relname;

\else
  do $$ begin
    raise exception 'local-licensing-role.sql needs -v stage=bootstrap or -v stage=grants';
  end $$;
\endif
