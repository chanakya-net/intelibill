#!/usr/bin/env bash
# Disposable PostgreSQL, explicit migration image, source licensing API and production admin web.
set -euo pipefail
umask 077

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$root"
artifacts="$(mktemp -d "${TMPDIR:-/tmp}/intelibill-p0110.XXXXXX")"
project="intelibill-p0110-${artifacts##*.}"
image="$(printf '%s' "$project" | tr '[:upper:]' '[:lower:]')-migrate:local"
api_dll="$root/src/backend/licensing/Intelibill.Licensing.Api/bin/Debug/net10.0/Intelibill.Licensing.Api.dll"
predecessor=20261114194123_AddAdminSignInSessions
migration=20261114202808_AddAdminRefreshCredentials
api_pid= web_pid= writer_pid= down_pid=

cleanup() {
  for pid in "$web_pid" "$api_pid" "$writer_pid" "$down_pid"; do
    if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  done
  docker rm -fv "$project-db" "$project-mail" >/dev/null 2>&1 || true
  docker network rm "$project" >/dev/null 2>&1 || true
  docker image rm "$image" >/dev/null 2>&1 || true
  rm -f "$artifacts/proof"
  rm -rf "$artifacts/keyring"
  # Playwright failure snapshots can contain fixture-only form values.
  if [[ -d "$artifacts/browser" ]]; then
    find "$artifacts/browser" -name error-context.md -type f -delete
  fi
}
trap cleanup EXIT

sql() {
  docker exec -i "$project-db" psql -XqAt -v ON_ERROR_STOP=1 -U postgres -d licensing "$@"
}

bundle() {
  docker run --rm --network "$project" \
    -e LicensingMigrationDatabase__Host=postgres -e LicensingMigrationDatabase__Database=licensing \
    -e LicensingMigrationDatabase__Username=licensing_migrator -e LicensingMigrationDatabase__Password=fixture-migrator \
    "$image" "$@"
}

wait_http() {
  for ((i=0;i<100;i++)); do
    if curl -fsS -o /dev/null "$1"; then return 0; fi
    sleep .2
  done 2>/dev/null
  printf 'Fixture endpoint not ready: %s\n' "$1" >&2
  return 1
}

latest() { sql -c 'SELECT max(migration_id) FROM "__EFMigrationsHistory"'; }
predecessor_facts() {
  sql -c "SELECT md5(concat(
    (SELECT to_jsonb(row(id,email,password_hash,verification_reference,verified_at,mfa_enrolled_at,deactivated_at))::text
      FROM admin_identities WHERE verification_reference='p0110-prior'),
    (SELECT string_agg(row_to_json(s)::text, '' ORDER BY s.id) FROM admin_sessions s
      JOIN admin_identities a ON a.id=s.admin_id WHERE a.verification_reference='p0110-prior'),
    (SELECT string_agg(row_to_json(e)::text, '' ORDER BY e.id) FROM provider_audit_events e)))"
}
schema_facts() {
  sql -c "SELECT concat_ws('|',
    (SELECT count(*) FROM pg_indexes WHERE tablename='admin_refresh_credentials' AND indexdef LIKE 'CREATE UNIQUE INDEX%'),
    (SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname='ck_bootstrap_audit_action'),
    (SELECT count(*) FROM pg_constraint WHERE conrelid='admin_refresh_credentials'::regclass AND contype IN ('c','f')),
    has_table_privilege('licensing_runtime','admin_refresh_credentials','INSERT,UPDATE,SELECT'))"
}

wait_for_sql() {
  for ((i=0;i<100;i++)); do
    if [[ "$(sql -c "$1")" == t ]]; then return 0; fi
    sleep .2
  done
  return 1
}

assert_down_fences_insert() {
  local fifo="$artifacts/credential-writer"
  mkfifo "$fifo"
  sql <"$fifo" >"$artifacts/credential-writer.log" 2>&1 &
  writer_pid=$!
  exec 3>"$fifo"
  cat >&3 <<'SQL'
SET application_name = 'p0110-credential-writer';
BEGIN;
INSERT INTO admin_refresh_credentials(id,session_id,token_hash,issued_at,expires_at)
SELECT 'bbf1e235-d19e-4b1e-a98a-573aff6fd6e4',id,repeat('A',64),now(),now()+interval '1 hour'
FROM admin_sessions;
SQL
  wait_for_sql "SELECT EXISTS (SELECT FROM pg_stat_activity WHERE application_name='p0110-credential-writer' AND state='idle in transaction')"
  bundle "$predecessor" >"$artifacts/down.log" 2>&1 &
  down_pid=$!
  # The predecessor job must reach the table fence while the staging insert still holds its write lock.
  wait_for_sql "SELECT EXISTS (SELECT FROM pg_locks WHERE relation='admin_refresh_credentials'::regclass AND mode='AccessExclusiveLock' AND NOT granted)"
  printf 'COMMIT;\n' >&3
  exec 3>&-
  wait "$writer_pid"
  writer_pid=
  if wait "$down_pid"; then
    printf 'Down unexpectedly removed the table after the staged credential committed.\n' >&2
    return 1
  fi
  down_pid=
  rg -q '55000: Admin refresh evidence exists' "$artifacts/down.log"
  [[ "$(latest)" == "$migration" && "$(schema_facts)" == "$schema" ]]
  [[ "$(sql -c "SELECT count(*) FROM admin_refresh_credentials WHERE id='bbf1e235-d19e-4b1e-a98a-573aff6fd6e4'")" == 1 ]]
  printf 'Down waited for a staged credential insert, refused with SQLSTATE 55000, and retained the row and migration history.\n'
  # This disposable fixture stages the row only for the Down check.
  sql -c "DELETE FROM admin_refresh_credentials WHERE id='bbf1e235-d19e-4b1e-a98a-573aff6fd6e4'"
}

printf 'P01-10 fixture output in %s\n' "$artifacts"
dotnet build src/backend/licensing/Intelibill.Licensing.Api -warnaserror >"$artifacts/api-build.log"
(cd src/admin-frontend && bun run build) >"$artifacts/web-build.log"
docker build -f src/backend/licensing/Dockerfile.migrate -t "$image" . >"$artifacts/bundle-build.log" 2>&1

docker network create "$project" >/dev/null
docker run -d --name "$project-db" --network "$project" --network-alias postgres \
  -p 127.0.0.1::5432 -e POSTGRES_PASSWORD=fixture-bootstrap postgres:17-alpine >/dev/null
docker run -d --name "$project-mail" -p 127.0.0.1::1025 -p 127.0.0.1::8025 axllent/mailpit:v1.33 >/dev/null
db_port="$(docker port "$project-db" 5432/tcp | cut -d: -f2)"
smtp_port="$(docker port "$project-mail" 1025/tcp | cut -d: -f2)"
mail="http://$(docker port "$project-mail" 8025/tcp)"
read -r api_port admin_port < <(python3 - <<'PY'
import socket
with socket.socket() as a, socket.socket() as b:
    a.bind(('127.0.0.1', 0))
    b.bind(('127.0.0.1', 0))
    print(a.getsockname()[1], b.getsockname()[1])
PY
)
api="http://127.0.0.1:$api_port"
admin="http://127.0.0.1:$admin_port"
for ((i=0;i<100;i++)); do
  if docker exec "$project-db" pg_isready -h 127.0.0.1 -U postgres >/dev/null; then break; fi
  sleep .2
done
docker exec -i "$project-db" psql -Xq -v ON_ERROR_STOP=1 -U postgres <<'SQL'
CREATE ROLE licensing_migrator LOGIN PASSWORD 'fixture-migrator';
CREATE ROLE licensing_runtime LOGIN PASSWORD 'fixture-runtime';
CREATE DATABASE licensing OWNER licensing_migrator;
REVOKE ALL ON DATABASE licensing FROM PUBLIC;
GRANT CONNECT ON DATABASE licensing TO licensing_runtime;
SQL
sql <<'SQL'
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO licensing_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE licensing_migrator IN SCHEMA public
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO licensing_runtime;
SQL
bundle "$predecessor" >"$artifacts/predecessor.log"
[[ "$(latest)" == "$predecessor" ]]
sql <<'SQL'
WITH prior AS (
  INSERT INTO admin_identities(id,email,password_hash,verification_reference,verified_at,mfa_enrolled_at)
  VALUES(gen_random_uuid(),'PRIOR@EXAMPLE.TEST',repeat('x',60),'p0110-prior',now()-interval '1 hour',now()-interval '1 hour')
  RETURNING id
)
INSERT INTO admin_sessions(id,admin_id,created_at,mfa_verified_at,last_seen_at)
SELECT gen_random_uuid(),id,now()-interval '10 minutes',now()-interval '10 minutes',now()-interval '5 minutes' FROM prior;
INSERT INTO provider_audit_events(id,action,occurred_at,owner_id)
SELECT gen_random_uuid(),14,now()-interval '10 minutes',id FROM admin_identities WHERE verification_reference='p0110-prior';
SQL
before="$(predecessor_facts)"

(
  cd src/backend/licensing/Intelibill.Licensing.Api
  export ASPNETCORE_ENVIRONMENT=Development
  export LicensingDatabase__Host=127.0.0.1 LicensingDatabase__Port="$db_port"
  export LicensingDatabase__Database=licensing LicensingDatabase__Username=licensing_runtime
  export LicensingDatabase__Password=fixture-runtime
  export AdminMail__Host=127.0.0.1 AdminMail__Port="$smtp_port" AdminMail__LocalCapture=true
  export AdminMail__FromEmail=provider-console@example.test
  export AdminBootstrap__DataProtectionKeyDirectory="$artifacts/keyring"
  export AdminMfa__ActiveKeyId=fixture AdminMfa__Keys__fixture="$(openssl rand -base64 32)"
  export AdminSessions__SigningKey="$(openssl rand -base64 32)"
  exec dotnet "$api_dll" --urls "$api"
) >"$artifacts/api.log" 2>&1 &
api_pid=$!
wait_http "$api/health/ready"
[[ "$(latest)" == "$predecessor" && "$(sql -c "SELECT to_regclass('public.admin_refresh_credentials') IS NULL")" == t ]]
printf 'API readiness returned 200 on P01-09 without applying schema changes.\n'
bundle >"$artifacts/upgrade.log"
[[ "$(latest)" == "$migration" && "$(predecessor_facts)" == "$before" ]]
schema="$(schema_facts)"
[[ "$schema" == "4|CHECK (((action >= 1) AND (action <= 16)))|5|t" ]]
[[ "$(sql -c "SELECT count(*) FROM admin_refresh_credentials")" == 0 ]]
printf 'Explicit migration job upgraded P01-09: prior identity, session and audit rows unchanged; schema %s.\n' "$schema"
printf 'The P01-09 session has no refresh credential, so it ends at access expiry and needs a fresh sign-in.\n'
assert_down_fences_insert

(
  export DOTNET_ENVIRONMENT=Development
  export LicensingMigrationDatabase__Host=127.0.0.1 LicensingMigrationDatabase__Port="$db_port"
  export LicensingMigrationDatabase__Database=licensing LicensingMigrationDatabase__Username=licensing_migrator
  export LicensingMigrationDatabase__Password=fixture-migrator
  dotnet "$api_dll" bootstrap-admin prepare --verified-email p0110-owner@example.test \
    --verification-reference p0110-local-fixture --admin-origin "$admin"
) >"$artifacts/proof"

PORT="$admin_port" API_ORIGIN="$api" ADMIN_WEB_ROOT="$root/src/admin-frontend/dist/ADMIN/browser" \
  node src/admin-frontend/server.mjs >"$artifacts/web.log" 2>&1 &
web_pid=$!
wait_http "$admin/"
wait_http "$mail/"
(
  cd src/admin-frontend
  export NODE_PATH="$PWD/node_modules"
  export P0110_ADMIN_WEB="$admin" P0110_PROOF_FILE="$artifacts/proof" P0110_ARTIFACTS="$artifacts"
  bun x playwright test --config ../../tests/backend/licensing/browser/playwright.p0110.config.ts
)

facts() {
  sql -c "SELECT concat_ws('|',
    (SELECT count(*) FROM admin_sessions), (SELECT count(*) FROM admin_sessions WHERE revoked_at IS NOT NULL),
    (SELECT count(*) FROM admin_refresh_credentials), (SELECT count(*) FROM admin_refresh_credentials WHERE consumed_at IS NULL),
    (SELECT count(*) FROM provider_audit_events WHERE action=16),
    (SELECT md5(string_agg(row_to_json(c)::text, '' ORDER BY c.id)) FROM admin_refresh_credentials c))"
}
settled="$(facts)"
IFS='|' read -r sessions revoked credentials unconsumed reuse _ <<<"$settled"
[[ "$sessions" == 5 && "$revoked" == 1 && "$credentials" -ge 8 && "$unconsumed" == 4 && "$reuse" == 1 ]]
[[ "$(sql -c "SELECT count(*) FROM admin_refresh_credentials WHERE parent_id IS NULL")" == 4 ]]
printf 'Browser run left %s sessions (%s revoked by reuse), %s chained credentials (%s unconsumed), %s reuse audit event.\n' \
  "$sessions" "$revoked" "$credentials" "$unconsumed" "$reuse"
bundle >"$artifacts/reapply.log"
[[ "$(latest)" == "$migration" && "$(predecessor_facts)" == "$before" && "$(facts)" == "$settled" && "$(schema_facts)" == "$schema" ]]
[[ "$(sql -c 'SELECT count(*) FROM "__EFMigrationsHistory"')" == 5 ]]
printf 'Repeated migration job changed nothing: history 5, predecessor facts, schema and refresh chain unchanged.\n'
