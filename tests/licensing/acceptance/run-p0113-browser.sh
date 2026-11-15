#!/usr/bin/env bash
# Disposable PostgreSQL, explicit migration image, source licensing API and production admin web.
set -euo pipefail
umask 077

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$root"
artifacts="$(mktemp -d "${TMPDIR:-/tmp}/intelibill-p0113.XXXXXX")"
artifacts="$(cd "$artifacts" && pwd -P)"
project="intelibill-p0113-${artifacts##*.}"
image="$(printf '%s' "$project" | tr '[:upper:]' '[:lower:]')-migrate:local"
api_dll="$root/src/licensing/Intelibill.Licensing.Api/bin/Debug/net10.0/Intelibill.Licensing.Api.dll"
predecessor=20261115002745_ProtectAdminRecovery
migration=20261115010116_AddAdminCapabilities
api_pid= web_pid=

cleanup() {
  for pid in "$web_pid" "$api_pid"; do
    if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  done
  docker rm -fv "$project-db" "$project-mail" >/dev/null 2>&1 || true
  docker network rm "$project" >/dev/null 2>&1 || true
  docker image rm "$image" >/dev/null 2>&1 || true
  rm -f "$artifacts/proof"
  rm -rf "$artifacts/keyring"
  if [[ -d "$artifacts/browser" ]]; then
    find "$artifacts/browser" -name error-context.md -type f -delete
  fi
}
trap cleanup EXIT

sql() {
  local database="$1"
  shift
  docker exec -i "$project-db" psql -XqAt -v ON_ERROR_STOP=1 -U postgres -d "$database" "$@"
}

bundle() {
  local database="$1"
  shift
  docker run --rm --network "$project" \
    -e LicensingMigrationDatabase__Host=postgres -e LicensingMigrationDatabase__Database="$database" \
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

latest() { sql "$1" -c 'SELECT max(migration_id) FROM "__EFMigrationsHistory"'; }

predecessor_facts() {
  sql licensing_upgrade <<'SQL'
SELECT md5(concat(
  (SELECT string_agg((to_jsonb(a) - 'capabilities')::text, '' ORDER BY id) FROM admin_identities a),
  (SELECT row_to_json(b)::text FROM initial_admin_bootstrap b),
  (SELECT row_to_json(t)::text FROM admin_totp t),
  (SELECT row_to_json(r)::text FROM admin_recovery_codes r),
  (SELECT row_to_json(s)::text FROM admin_sessions s),
  (SELECT row_to_json(e)::text FROM provider_audit_events e)));
SQL
}

prepare_upgrade() {
  sql licensing_upgrade <<'SQL'
INSERT INTO admin_identities(id,email,password_hash,verification_reference,verified_at,mfa_enrolled_at) VALUES
('00000000-0000-0000-0000-000000008431','PRIOR-OWNER@EXAMPLE.TEST',repeat('x',60),'prior-owner',now(),now()),
('00000000-0000-0000-0000-000000008432','PRIOR-OPERATOR@EXAMPLE.TEST',repeat('x',60),'prior-operator',now(),now());
UPDATE initial_admin_bootstrap SET owner_id='00000000-0000-0000-0000-000000008431', consumed_at=now(),
  proof_digest=repeat('B',64), verified_email='PRIOR-OWNER@EXAMPLE.TEST', verification_reference='prior-owner',
  admin_origin='https://localhost', prepared_at=now()-interval '1 minute', expires_at=now()+interval '29 minutes';
INSERT INTO admin_totp(admin_id,key_id,encrypted_secret,last_accepted_step,attempts)
VALUES ('00000000-0000-0000-0000-000000008431','prior',decode(repeat('ab',48),'hex'),1,0);
INSERT INTO admin_recovery_codes(admin_id,digest) VALUES ('00000000-0000-0000-0000-000000008431',repeat('C',64));
INSERT INTO admin_sessions(id,admin_id,created_at,mfa_verified_at,last_seen_at)
VALUES (gen_random_uuid(),'00000000-0000-0000-0000-000000008431',now(),now(),now());
INSERT INTO provider_audit_events(id,action,occurred_at,owner_id,actor_kind,reason,correlation_id)
VALUES (gen_random_uuid(),14,now(),'00000000-0000-0000-0000-000000008431',4,13,gen_random_uuid());
SQL
}

start_api() {
  cd "$(dirname "$api_dll")"
  export ASPNETCORE_ENVIRONMENT=Development
  export LicensingDatabase__Host=127.0.0.1 LicensingDatabase__Port="$db_port"
  export LicensingDatabase__Database=licensing LicensingDatabase__Username=licensing_runtime
  export LicensingDatabase__Password=fixture-runtime
  export AdminMail__Host=127.0.0.1 AdminMail__Port="$smtp_port" AdminMail__LocalCapture=true
  export AdminMail__FromEmail=provider-console@example.test
  export AdminBootstrap__DataProtectionKeyDirectory="$artifacts/keyring"
  export AdminMfa__ActiveKeyId=fixture AdminMfa__Keys__fixture="$(openssl rand -base64 32)"
  export AdminSessions__SigningKey="$(openssl rand -base64 32)"
  export AdminFixtures__AdminId=00000000-0000-0000-0000-000000008433
  export AdminFixtures__SessionDigest="$(printf '%s' 'p0113-local-scope-fixture' | shasum -a 256 | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')"
  export AdminFixtures__Capabilities=admin.access.manage
  exec dotnet "$api_dll" --urls "$api"
}

printf 'P01-13 fixture output in %s\n' "$artifacts"
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
    for listener in (a, b):
        listener.bind(('127.0.0.1', 0))
    print(*(listener.getsockname()[1] for listener in (a, b)))
PY
)
api="http://127.0.0.1:$api_port"
admin="http://127.0.0.1:$admin_port"
for ((i=0;i<100;i++)); do
  if docker exec "$project-db" pg_isready -h 127.0.0.1 -U postgres >/dev/null; then break; fi
  sleep .2
done
sql postgres <<'SQL'
CREATE ROLE licensing_migrator LOGIN PASSWORD 'fixture-migrator';
CREATE ROLE licensing_runtime LOGIN PASSWORD 'fixture-runtime';
CREATE DATABASE licensing OWNER licensing_migrator;
CREATE DATABASE licensing_upgrade OWNER licensing_migrator;
REVOKE ALL ON DATABASE licensing, licensing_upgrade FROM PUBLIC;
GRANT CONNECT ON DATABASE licensing TO licensing_runtime;
SQL
sql licensing <<'SQL'
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO licensing_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE licensing_migrator IN SCHEMA public
GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON TABLES TO licensing_runtime;
SQL
bundle licensing >"$artifacts/fresh.log"
[[ "$(latest licensing)" == "$migration" ]]
printf 'Explicit migration job applied the full schema to a fresh PostgreSQL database.\n'
bundle licensing_upgrade "$predecessor" >"$artifacts/predecessor.log"
[[ "$(latest licensing_upgrade)" == "$predecessor" ]]
prepare_upgrade
before="$(predecessor_facts)"
bundle licensing_upgrade >"$artifacts/upgrade.log"
[[ "$(latest licensing_upgrade)" == "$migration" && "$(predecessor_facts)" == "$before" ]]
[[ "$(sql licensing_upgrade -c "SELECT string_agg(capabilities::text, ',' ORDER BY id) FROM admin_identities")" == '16,0' ]]
printf 'Explicit upgrade from P01-12 preserved bootstrap, identity, authenticator, recovery code, session and audit facts. Only the consumed owner received a grant.\n'

(
  export DOTNET_ENVIRONMENT=Development
  export LicensingMigrationDatabase__Host=127.0.0.1 LicensingMigrationDatabase__Port="$db_port"
  export LicensingMigrationDatabase__Database=licensing LicensingMigrationDatabase__Username=licensing_migrator
  export LicensingMigrationDatabase__Password=fixture-migrator
  dotnet "$api_dll" bootstrap-admin prepare --verified-email p0112-owner@example.test \
    --verification-reference p0113-local-fixture --admin-origin "$admin"
) >"$artifacts/proof"

start_api >"$artifacts/api.log" 2>&1 &
api_pid=$!
PORT="$admin_port" API_ORIGIN="$api" ADMIN_WEB_ROOT="$artifacts/admin/browser" \
  node src/admin-frontend/server.mjs >"$artifacts/web.log" 2>&1 &
web_pid=$!
wait_http "$api/health/ready"
wait_http "$admin/"
wait_http "$mail/"
(
  cd src/admin-frontend
  export NODE_PATH="$PWD/node_modules"
  export P0112_ADMIN_WEB="$admin" P0112_MAILPIT="$mail" P0112_PROOF_FILE="$artifacts/proof"
  export P0113_ARTIFACTS="$artifacts" P0113_DB_CONTAINER="$project-db"
  bun x playwright test --config ../../tests/licensing/browser/playwright.p0113.config.ts
)

[[ "$(sql licensing -c "SELECT count(*) FROM admin_identities WHERE capabilities=16 AND deactivated_at IS NULL")" == 1 ]]
[[ "$(sql licensing -c "SELECT count(*) FROM admin_identities WHERE capabilities=2 AND deactivated_at IS NULL")" == 1 ]]
[[ "$(sql licensing -c "SELECT count(*) FROM provider_audit_events WHERE action=105
  AND (\"before\"->>'Version')::bigint <> (\"after\"->>'Version')::bigint")" == 3 ]]
[[ "$(sql licensing -c "SELECT count(*) FROM provider_audit_events WHERE action=107 AND reason=30")" == 1 ]]
if bundle licensing "$predecessor" >"$artifacts/down.log" 2>&1; then exit 1; fi
rg -q '55000: Administrator capability evidence exists' "$artifacts/down.log"
[[ "$(latest licensing)" == "$migration" ]]
printf 'Persisted one owner and one Billing-only administrator, three versioned changes and a last-owner denial. Migration Down refused with SQLSTATE 55000.\n'
