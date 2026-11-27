#!/usr/bin/env bash
# Explicit migrations, source licensing API and built admin web on a disposable local fixture.
set -euo pipefail
umask 077

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$root"
artifacts="$(mktemp -d "${TMPDIR:-/tmp}/intelibill-p0114.XXXXXX")"
project="intelibill-p0114-${artifacts##*.}"
api_dll="$root/src/backend/licensing/Intelibill.Licensing.Api/bin/Debug/net10.0/Intelibill.Licensing.Api.dll"
predecessor=20261114202808_AddAdminRefreshCredentials
api_pid= web_pid=

cleanup() {
  for pid in "$web_pid" "$api_pid"; do
    if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  done
  docker rm -fv "$project-db" "$project-mail" >/dev/null 2>&1 || true
  rm -f "$artifacts/proof"
  rm -rf "$artifacts/keyring"
  if [[ -d "$artifacts/browser" ]]; then
    find "$artifacts/browser" -name error-context.md -type f -delete
  fi
}
trap cleanup EXIT

sql() {
  docker exec -i "$project-db" psql -XqAt -v ON_ERROR_STOP=1 -U postgres -d licensing "$@"
}

migration() {
  dotnet ef database update "$@" --no-build \
    --project src/backend/licensing/Intelibill.Licensing.Infrastructure \
    --startup-project src/backend/licensing/Intelibill.Licensing.Api
}

wait_http() {
  for ((i=0;i<100;i++)); do
    if curl -fsS -o /dev/null "$1"; then return 0; fi
    sleep .2
  done 2>/dev/null
  printf 'Fixture endpoint not ready: %s\n' "$1" >&2
  return 1
}

legacy_digest() {
  sql -c "SELECT md5(row_to_json(row(id,action,occurred_at,owner_id,invitation_id))::text)
    FROM provider_audit_events WHERE id='00000000-0000-0000-0000-000000008444'"
}

printf 'P01-14 fixture output in %s\n' "$artifacts"
dotnet build src/backend/licensing/Intelibill.Licensing.Api -warnaserror >"$artifacts/api-build.log"
(cd src/frontend/licensing && bun run build) >"$artifacts/web-build.log" 2>&1
docker run -d --name "$project-db" -p 127.0.0.1::5432 \
  -e POSTGRES_PASSWORD=fixture-bootstrap postgres:17-alpine >/dev/null
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
GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON TABLES TO licensing_runtime;
SQL

export LicensingMigrationDatabase__Host=127.0.0.1 LicensingMigrationDatabase__Port="$db_port"
export LicensingMigrationDatabase__Database=licensing LicensingMigrationDatabase__Username=licensing_migrator
export LicensingMigrationDatabase__Password=fixture-migrator
migration "$predecessor" >"$artifacts/predecessor.log"
[[ "$(sql -c 'SELECT max(migration_id) FROM "__EFMigrationsHistory"')" == "$predecessor" ]]
sql -c "INSERT INTO provider_audit_events (id,action,occurred_at)
  VALUES ('00000000-0000-0000-0000-000000008444', 4, '2026-01-01T00:00:00Z')"
before="$(legacy_digest)"
migration >"$artifacts/upgrade.log"
[[ "$(legacy_digest)" == "$before" ]]
[[ "$(sql -c 'SELECT max(migration_id) FROM "__EFMigrationsHistory"')" == *_AddProviderAuditInspection ]]
sql -c "SET ROLE licensing_runtime; INSERT INTO provider_audit_events(id,action,occurred_at)
  SELECT gen_random_uuid(),4,now()-interval '1 day' FROM generate_series(1,30)"

(
  export DOTNET_ENVIRONMENT=Development
  dotnet "$api_dll" bootstrap-admin prepare --verified-email p0114-owner@example.test \
    --verification-reference p0114-attestation-canary --admin-origin "$admin"
) >"$artifacts/proof"

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
PORT="$admin_port" API_ORIGIN="$api" ADMIN_WEB_ROOT="$root/src/frontend/licensing/dist/ADMIN/browser" \
  node src/frontend/licensing/server.mjs >"$artifacts/web.log" 2>&1 &
web_pid=$!
wait_http "$admin/"
wait_http "$mail/"
(
  cd src/frontend/licensing
  export NODE_PATH="$PWD/node_modules"
  export P0114_ADMIN_WEB="$admin" P0114_MAILPIT="$mail" P0114_PROOF_FILE="$artifacts/proof" P0114_ARTIFACTS="$artifacts"
  bun x playwright test --config ../../../tests/backend/licensing/browser/playwright.p0114.config.ts
)

for statement in 'UPDATE provider_audit_events SET action=action' 'DELETE FROM provider_audit_events' 'TRUNCATE provider_audit_events'; do
  if sql -c "SET ROLE licensing_runtime; $statement" >>"$artifacts/immutable.log" 2>&1; then exit 1; fi
done
[[ "$(rg -c 'Provider audit is append-only' "$artifacts/immutable.log")" == 3 ]]
if migration "$predecessor" >"$artifacts/down.log" 2>&1; then exit 1; fi
rg -q '55000: Provider audit inspection evidence exists' "$artifacts/down.log"
migration >"$artifacts/reapply.log"
rg -q 'already up to date' "$artifacts/reapply.log"
[[ "$(legacy_digest)" == "$before" ]]
[[ "$(sql -c 'SELECT count(*) FROM provider_audit_events WHERE action=8 AND "before" IS NOT NULL AND "after" IS NOT NULL')" == 1 ]]
[[ "$(sql -c 'SELECT count(*) > 0 FROM provider_audit_events WHERE action=101')" == t ]]
[[ "$(sql -c 'SELECT count(*) FROM provider_audit_events WHERE action=102')" == 1 ]]
printf 'Predecessor history digest survived upgrade and reapply. Runtime INSERT/SELECT succeeded, UPDATE/DELETE/TRUNCATE were refused, and Down refused to discard enriched evidence.\n'
