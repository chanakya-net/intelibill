#!/usr/bin/env bash
# Disposable PostgreSQL, explicit migration image, source licensing API and production admin web.
set -euo pipefail
umask 077

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$root"
artifacts="$(mktemp -d "${TMPDIR:-/tmp}/intelibill-p0111.XXXXXX")"
project="intelibill-p0111-${artifacts##*.}"
image="$(printf '%s' "$project" | tr '[:upper:]' '[:lower:]')-migrate:local"
api_dll="$root/src/licensing/Intelibill.Licensing.Api/bin/Debug/net10.0/Intelibill.Licensing.Api.dll"
predecessor=20261114221354_AddProviderAuditInspection
migration=20261114225609_AddAdminSessionRevocationAudit
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
    -e LicensingMigrationDatabase__Host=postgres -e LicensingMigrationDatabase__Database="${fixture_database:-licensing}" \
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
    (SELECT row_to_json(a)::text FROM admin_identities a WHERE verification_reference='p0111-prior'),
    (SELECT row_to_json(s)::text FROM admin_sessions s JOIN admin_identities a ON a.id=s.admin_id
      WHERE a.verification_reference='p0111-prior'),
    (SELECT row_to_json(c)::text FROM admin_refresh_credentials c JOIN admin_sessions s ON s.id=c.session_id
      JOIN admin_identities a ON a.id=s.admin_id WHERE a.verification_reference='p0111-prior'),
    (SELECT row_to_json(e)::text FROM provider_audit_events e WHERE id='00000000-0000-0000-0000-000000008411')))"
}

wait_for_sql() {
  for ((i=0;i<100;i++)); do
    if [[ "$(sql -c "$1")" == t ]]; then return 0; fi
    sleep .2
  done
  return 1
}

assert_down_fences_insert() {
  local fifo="$artifacts/audit-writer"
  mkfifo "$fifo"
  sql <"$fifo" >"$artifacts/audit-writer.log" 2>&1 &
  writer_pid=$!
  exec 3>"$fifo"
  cat >&3 <<'SQL'
SET application_name='p0111-audit-writer';
BEGIN;
SET ROLE licensing_runtime;
INSERT INTO provider_audit_events(id,action,occurred_at,actor_kind,reason,correlation_id)
VALUES ('00000000-0000-0000-0000-000000008412',20,now(),4,18,gen_random_uuid());
SQL
  wait_for_sql "SELECT EXISTS (SELECT FROM pg_stat_activity WHERE application_name='p0111-audit-writer' AND state='idle in transaction')"
  bundle "$predecessor" >"$artifacts/down.log" 2>&1 &
  down_pid=$!
  wait_for_sql "SELECT EXISTS (SELECT FROM pg_locks WHERE relation='provider_audit_events'::regclass AND mode='AccessExclusiveLock' AND NOT granted)"
  printf 'COMMIT;\n' >&3
  exec 3>&-
  wait "$writer_pid"
  writer_pid=
  if wait "$down_pid"; then return 1; fi
  down_pid=
  rg -q '55000: Administrator session audit evidence exists' "$artifacts/down.log"
  [[ "$(latest)" == "$migration" ]]
  [[ "$(sql -c "SELECT count(*) FROM provider_audit_events WHERE id='00000000-0000-0000-0000-000000008412'")" == 1 ]]
  printf 'Down waited for a staged runtime audit INSERT, refused with SQLSTATE 55000 and retained the row and migration history.\n'
}

printf 'P01-11 fixture output in %s\n' "$artifacts"
dotnet build src/licensing/Intelibill.Licensing.Api -warnaserror >"$artifacts/api-build.log"
(cd src/admin-frontend && bun run build --output-path "$artifacts/admin") >"$artifacts/web-build.log"
docker build -f src/licensing/Dockerfile.migrate -t "$image" . >"$artifacts/bundle-build.log" 2>&1

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
CREATE DATABASE licensing_fresh OWNER licensing_migrator;
REVOKE ALL ON DATABASE licensing FROM PUBLIC;
GRANT CONNECT ON DATABASE licensing TO licensing_runtime;
SQL
sql <<'SQL'
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO licensing_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE licensing_migrator IN SCHEMA public
GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON TABLES TO licensing_runtime;
SQL
fixture_database=licensing_fresh bundle >"$artifacts/fresh.log"
[[ "$(docker exec "$project-db" psql -XqAt -U postgres -d licensing_fresh -c 'SELECT max(migration_id) FROM "__EFMigrationsHistory"')" == "$migration" ]]
printf 'Explicit migration job applied the full licensing schema to a fresh database.\n'
bundle "$predecessor" >"$artifacts/predecessor.log"
[[ "$(latest)" == "$predecessor" ]]
sql <<'SQL'
WITH prior AS (
  INSERT INTO admin_identities(id,email,password_hash,verification_reference,verified_at,mfa_enrolled_at)
  VALUES(gen_random_uuid(),'PRIOR@EXAMPLE.TEST',repeat('x',60),'p0111-prior',now()-interval '1 hour',now()-interval '1 hour')
  RETURNING id
)
INSERT INTO admin_sessions(id,admin_id,created_at,mfa_verified_at,last_seen_at)
SELECT gen_random_uuid(),id,now()-interval '10 minutes',now()-interval '10 minutes',now()-interval '5 minutes' FROM prior;
INSERT INTO admin_refresh_credentials(id,session_id,token_hash,issued_at,expires_at)
SELECT gen_random_uuid(),id,repeat('A',64),created_at,created_at+interval '12 hours' FROM admin_sessions;
INSERT INTO provider_audit_events(id,action,occurred_at,owner_id,actor_kind,reason,correlation_id)
SELECT '00000000-0000-0000-0000-000000008411',14,now()-interval '10 minutes',id,4,13,gen_random_uuid()
FROM admin_identities WHERE verification_reference='p0111-prior';
SQL
before="$(predecessor_facts)"

(
  cd src/licensing/Intelibill.Licensing.Api
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
[[ "$(latest)" == "$predecessor" ]]
printf 'Source API became ready on P01-14 without applying the new migration.\n'
bundle >"$artifacts/upgrade.log"
[[ "$(latest)" == "$migration" && "$(predecessor_facts)" == "$before" ]]
[[ "$(sql -c "SELECT count(*) FROM admin_sessions WHERE xmin::text::bigint > 0")" == 1 ]]
[[ "$(sql -c "SELECT count(*) FROM pg_attribute WHERE attrelid='admin_sessions'::regclass AND attname='xmin' AND attnum < 0")" == 1 ]]
printf 'Explicit migration job upgraded P01-14 and preserved predecessor identity, session, refresh and audit facts.\n'
assert_down_fences_insert

(
  export DOTNET_ENVIRONMENT=Development
  export LicensingMigrationDatabase__Host=127.0.0.1 LicensingMigrationDatabase__Port="$db_port"
  export LicensingMigrationDatabase__Database=licensing LicensingMigrationDatabase__Username=licensing_migrator
  export LicensingMigrationDatabase__Password=fixture-migrator
  dotnet "$api_dll" bootstrap-admin prepare --verified-email p0111-owner@example.test \
    --verification-reference p0111-local-fixture --admin-origin "$admin"
) >"$artifacts/proof"

PORT="$admin_port" API_ORIGIN="$api" ADMIN_WEB_ROOT="$artifacts/admin/browser" \
  node src/admin-frontend/server.mjs >"$artifacts/web.log" 2>&1 &
web_pid=$!
wait_http "$admin/"
wait_http "$mail/"
(
  cd src/admin-frontend
  export NODE_PATH="$PWD/node_modules"
  export P0111_ADMIN_WEB="$admin" P0111_MAILPIT="$mail" P0111_PROOF_FILE="$artifacts/proof" P0111_ARTIFACTS="$artifacts"
  bun x playwright test --config ../../tests/licensing/browser/playwright.p0111.config.ts
)

for statement in 'UPDATE provider_audit_events SET action=action' 'DELETE FROM provider_audit_events' 'TRUNCATE provider_audit_events'; do
  if sql -c "SET ROLE licensing_runtime; $statement" >>"$artifacts/immutable.log" 2>&1; then exit 1; fi
done
[[ "$(rg -c 'Provider audit is append-only' "$artifacts/immutable.log")" == 3 ]]
[[ "$(sql -c "SELECT count(*) FROM provider_audit_events WHERE action=18 AND \"before\"->>'Version' <> \"after\"->>'Version'")" == 1 ]]
[[ "$(sql -c "SELECT count(*) FROM provider_audit_events WHERE action=19 AND \"after\"->>'State' = '9'")" == 2 ]]
[[ "$(sql -c "SELECT count(*) FROM admin_sessions WHERE revoked_at IS NOT NULL")" == 3 ]]
settled="$(sql -c "SELECT md5(string_agg(row_to_json(e)::text, '' ORDER BY e.id)) FROM provider_audit_events e")"
bundle >"$artifacts/reapply.log"
rg -q 'already up to date' "$artifacts/reapply.log"
[[ "$(latest)" == "$migration" && "$(predecessor_facts)" == "$before" ]]
[[ "$(sql -c "SELECT md5(string_agg(row_to_json(e)::text, '' ORDER BY e.id)) FROM provider_audit_events e")" == "$settled" ]]
printf 'Three revoked families and their audit survived reapply. Runtime UPDATE, DELETE and TRUNCATE were refused.\n'
