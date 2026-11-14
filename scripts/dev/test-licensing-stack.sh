#!/usr/bin/env bash
# Acceptance harness for the combined local stack (issue #835, P01-05).
#
# Runs the real `docker compose --profile full up -d --build` workflow under
# isolated Compose projects (own container names, volumes, network and
# loopback ports), so a developer's default `intelibill` stack, its containers
# and its postgres_data volume are never touched.
#
#   P0105_PROJECT=intelibill-p01-05-dev P0105_ARTIFACTS=/tmp/p0105 \
#     bash scripts/dev/test-licensing-stack.sh --case all
#
# Cases (default all):
#   fresh     fresh volume: one-shots, probes, isolated roles, no runtime DDL,
#             repeated up, API restart, down/up retention, branch independence
#   reused    an existing merchant-only volume gains licensing without losing
#             merchant data; concurrent and refused (Production) preparation
#   failure   a failing migrator blocks only its own API/web branch
#   browser   Chromium against the real images: merchant enrollment through
#             Mailpit, sign-in and shop persistence across restart; admin denial
#   all       every case above
#   cleanup   remove this run's own projects and volumes (label-checked)
#
# The main project is left running with its data so the demo can be inspected;
# `--case cleanup` removes it. Evidence lands in P0105_ARTIFACTS.
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${repository_root}"
main_project="${P0105_PROJECT:-intelibill-p01-05-local}"
project="${main_project}"
evidence="${P0105_ARTIFACTS:-/tmp/${main_project}-evidence}"
mkdir -p "${evidence}"
# shellcheck source=scripts/dev/licensing-stack-lib.sh
source "${repository_root}/scripts/dev/licensing-stack-lib.sh"

case_name="all"
while (($#)); do
  case "$1" in
    --case) case_name="$2"; shift 2 ;;
    *) fail "unknown argument: $1" ;;
  esac
done

licensing_history() {
  sql intelibill_licensing_dev -c "select case when to_regclass('public.\"__EFMigrationsHistory\"') is null then 'absent' else 'present' end"
}

stack_up() {
  log "compose up --build (${project})"
  compose up -d --build >>"${evidence}/${project}-compose.log" 2>&1 || true
  wait_stack
}

check_probes() {
  expect_http "${merchant_api}/health/live" 200
  expect_http "${merchant_api}/health/ready" 200
  expect_http "${licensing_api}/health/live" 200
  expect_http "${licensing_api}/health/ready" 200
  expect_http "${merchant_web}/" 200
  expect_http "${admin_web}/" 200
  expect_http "${admin_web}/api/shops/me" 404
  expect_http "${mailpit_ui}/api/v1/messages" 200
  curl -s -D - -o /dev/null "${licensing_api}/health/ready" | grep -qi '^cache-control: no-store' || fail "licensing readiness is cacheable"
}

check_roles() {
  local attributes
  attributes="$(sql postgres -c "select rolname, rolsuper, rolbypassrls, rolcreatedb, rolcreaterole, rolreplication, rolinherit
    from pg_roles where rolname in ('intelibill_app', 'intelibill_licensing_app') order by rolname")"
  record roles-attributes.txt "${attributes}"
  [[ "${attributes}" == $'intelibill_app|f|f|f|f|f|f\nintelibill_licensing_app|f|f|f|f|f|f' ]] \
    || fail "runtime role attributes: ${attributes}"
  pass "runtime roles NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT"
  [[ "$(sql postgres -c "select count(*) from pg_auth_members m join pg_roles r on r.oid in (m.member, m.roleid)
    where r.rolname in ('intelibill_app', 'intelibill_licensing_app')")" == 0 ]] || fail "runtime role has a membership"
  [[ "$(sql postgres -c "select pg_get_userbyid(datdba) from pg_database where datname = 'intelibill_dev'")" == intelibill ]] \
    || fail "merchant database owner changed"
  [[ "$(sql postgres -c "select pg_get_userbyid(datdba) from pg_database where datname = 'intelibill_licensing_dev'")" == intelibill_licensing_migrator ]] \
    || fail "licensing database owner is not its migrator"
  local merchant=(intelibill_app intelibill_app_password) licensing=(intelibill_licensing_app intelibill_licensing_app_password)
  expect_denied "merchant runtime -> licensing database" 'permission denied for database' "${merchant[@]}" intelibill_licensing_dev 'select 1'
  expect_denied "licensing runtime -> merchant database" 'permission denied for database' "${licensing[@]}" intelibill_dev 'select 1'
  expect_denied "merchant runtime CREATE TABLE" 'permission denied for schema' "${merchant[@]}" intelibill_dev 'create table p0105_x(i int)'
  expect_denied "licensing runtime CREATE TABLE" 'permission denied for schema' "${licensing[@]}" intelibill_licensing_dev 'create table p0105_x(i int)'
  expect_denied "merchant runtime TEMP" 'temporary tables' "${merchant[@]}" intelibill_dev 'create temp table p0105_x(i int)'
  expect_denied "licensing runtime TEMP" 'temporary tables' "${licensing[@]}" intelibill_licensing_dev 'create temp table p0105_x(i int)'
  expect_denied "merchant runtime SET ROLE owner" 'permission denied to set role' "${merchant[@]}" intelibill_dev 'set role intelibill'
  expect_denied "licensing runtime SET ROLE migrator" 'permission denied to set role' "${licensing[@]}" intelibill_licensing_dev 'set role intelibill_licensing_migrator'
  expect_denied "merchant runtime ALTER owned table" 'must be owner|permission denied' "${merchant[@]}" intelibill_dev 'alter table shops disable row level security'
  [[ "$(sql intelibill_dev -c "select count(*) from pg_class where relnamespace = 'public'::regnamespace and relrowsecurity")" -gt 0 ]] \
    || fail "merchant tables carry no row-level security"
  pass "merchant tables keep row-level security; runtime cannot alter them"
}

check_sentinels_and_grants() {
  # Future-object path: the migrator creates a table after the grants job ran;
  # default privileges FOR ROLE the migrator must reach the runtime login.
  sql_as intelibill_licensing_migrator intelibill_licensing_migrator_password intelibill_licensing_dev \
    "create table if not exists p0105_sentinel(note text primary key); insert into p0105_sentinel values ('licensing-sentinel') on conflict do nothing" >/dev/null \
    || fail "licensing migrator cannot create its probe"
  sql intelibill_dev -c "create table if not exists p0105_sentinel(note text primary key); insert into p0105_sentinel values ('merchant-sentinel') on conflict do nothing" >/dev/null
  sql_as intelibill_licensing_app intelibill_licensing_app_password intelibill_licensing_dev \
    "insert into p0105_sentinel values ('runtime-write') on conflict do nothing; select count(*) from p0105_sentinel" >/dev/null \
    || fail "licensing runtime cannot use a table its migrator created"
  sql_as intelibill_app intelibill_app_password intelibill_dev "select note from p0105_sentinel" | grep -q merchant-sentinel \
    || fail "merchant runtime cannot read a table its migrator created"
  pass "runtime logins use tables created after grants (default privileges FOR ROLE the migrator)"
}

snapshot() {
  printf 'merchant_history=%s licensing_history=%s merchant_sentinel=%s licensing_sentinel=%s licensing_owner=%s\n' \
    "$(sql intelibill_dev -c 'select count(*) from "__EFMigrationsHistory"')" \
    "$(licensing_history)" \
    "$(sql intelibill_dev -c 'select count(*) from p0105_sentinel')" \
    "$(sql intelibill_licensing_dev -c 'select count(*) from p0105_sentinel')" \
    "$(sql postgres -c "select pg_get_userbyid(datdba) from pg_database where datname = 'intelibill_licensing_dev'")"
}

enable_ddl_log() {
  sql postgres -c "alter system set log_statement = 'ddl'" -c "alter system set log_line_prefix = '%m [%p] user=%u db=%d '" \
    -c "select pg_reload_conf()" >/dev/null
}

check_no_runtime_ddl() {
  local since postgres_id runtime_ddl control i
  enable_ddl_log
  since="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  sleep 1
  sql_as intelibill_licensing_migrator intelibill_licensing_migrator_password intelibill_licensing_dev \
    'create table p0105_ddl_control(i int); drop table p0105_ddl_control' >/dev/null
  compose restart backend licensing-api >>"${evidence}/${project}-compose.log" 2>&1
  wait_for backend healthy
  wait_for licensing-api healthy
  for i in 1 2 3 4 5; do
    expect_http "${licensing_api}/health/ready" 200 >/dev/null
    expect_http "${merchant_api}/health/ready" 200 >/dev/null
  done
  postgres_id="$(container_of postgres)"
  docker logs --since "${since}" "${postgres_id}" >"${evidence}/postgres-ddl.log" 2>&1
  control="$(grep -c 'user=intelibill_licensing_migrator .*statement: create table p0105_ddl_control' "${evidence}/postgres-ddl.log" || true)"
  runtime_ddl="$(grep -Ec 'user=(intelibill_app|intelibill_licensing_app) .*statement:' "${evidence}/postgres-ddl.log" || true)"
  [[ "${control}" -ge 1 ]] || fail "DDL logging control statement was not captured"
  [[ "${runtime_ddl}" -eq 0 ]] || fail "runtime identities issued ${runtime_ddl} DDL statements"
  pass "DDL log: migrator control captured (${control}); runtime DDL across concurrent restarts and probes = 0"
}

check_retention() {
  local before after
  before="$(snapshot)"
  log "snapshot before: ${before}"
  local jobs="db-bootstrap merchant-migrate db-runtime-role licensing-migrate licensing-runtime-role" job started
  started="$(for job in ${jobs}; do state_of "${job}" | awk -v j="${job}" '{ print j, $NF }'; done)"
  compose up -d >>"${evidence}/${project}-compose.log" 2>&1
  wait_stack
  for job in ${jobs}; do
    ! grep -Fqx "${job} $(state_of "${job}" | awk '{ print $NF }')" <<<"${started}" || fail "${job} did not run again on up"
  done
  [[ "$(snapshot)" == "${before}" ]] || fail "repeated up changed data: $(snapshot)"
  pass "repeated up: every one-shot ran again and exited 0; data and history unchanged"
  compose down >>"${evidence}/${project}-compose.log" 2>&1
  compose up -d >>"${evidence}/${project}-compose.log" 2>&1
  wait_stack
  after="$(snapshot)"
  [[ "${after}" == "${before}" ]] || fail "down/up lost data: ${after}"
  pass "down (no -v) then up: ${after}"
}

check_independence() {
  compose stop licensing-api admin-frontend >>"${evidence}/${project}-compose.log" 2>&1
  expect_http "${merchant_api}/health/ready" 200
  expect_http "${merchant_web}/" 200
  compose start licensing-api admin-frontend >>"${evidence}/${project}-compose.log" 2>&1
  wait_for licensing-api healthy
  compose stop backend frontend >>"${evidence}/${project}-compose.log" 2>&1
  expect_http "${licensing_api}/health/ready" 200
  wait_http "${admin_web}/" 200
  compose start backend frontend >>"${evidence}/${project}-compose.log" 2>&1
  wait_for backend healthy
  wait_http "${merchant_web}/" 200
  pass "each API/web pair serves while the other is stopped"
}

case_fresh() {
  project="${main_project}"
  guard_project
  assign_ports 14000
  stack_up
  compose config --format json | sed -E 's/("[A-Za-z_]*(Password|PASSWORD|Secret|Key)[A-Za-z_]*"?[=:] ?")[^"]*/\1<redacted>/g' >"${evidence}/resolved-compose.json"
  compose ps -a --format '{{.Service}} {{.State}} {{.ExitCode}} {{.Status}}' >"${evidence}/fresh-states.txt"
  check_probes
  check_roles
  check_sentinels_and_grants
  check_no_runtime_ddl
  check_retention
  check_independence
}

source "${repository_root}/scripts/dev/licensing-stack-cases.sh"

case "${case_name}" in
  fresh) case_fresh ;;
  reused) case_reused ;;
  failure) case_failure ;;
  browser) case_browser ;;
  cleanup) case_cleanup ;;
  all)
    case_fresh
    case_reused
    case_failure
    case_browser
    ;;
  *) fail "unknown case: ${case_name}" ;;
esac
log "case ${case_name} complete; evidence in ${evidence}"
