#!/usr/bin/env bash
# Disposable PostgreSQL, explicit migration image, source licensing API and production admin web.
set -euo pipefail
umask 077

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$root"
artifacts="$(mktemp -d "${TMPDIR:-/tmp}/intelibill-p0109.XXXXXX")"
project="intelibill-p0109-${artifacts##*.}"
image="$(printf '%s' "$project" | tr '[:upper:]' '[:lower:]')-migrate:local"
api_dll="$root/src/backend/licensing/Intelibill.Licensing.Api/bin/Debug/net10.0/Intelibill.Licensing.Api.dll"
predecessor=20261114185900_AddAdminMfaEnrollment
migration=20261114194123_AddAdminSignInSessions
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
    (SELECT to_jsonb(row(id,email,password_hash,verification_reference,verified_at,mfa_enrolled_at))::text
      FROM admin_identities WHERE verification_reference='p0109-prior'),
    (SELECT row_to_json(c)::text FROM bootstrap_enrollment_credentials c
      JOIN admin_identities a ON a.id=c.owner_id WHERE a.verification_reference='p0109-prior')))"
}

printf 'P01-09 fixture output in %s\n' "$artifacts"
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
  INSERT INTO admin_identities(id,email,password_hash,verification_reference,verified_at)
  VALUES(gen_random_uuid(),'PRIOR@EXAMPLE.TEST',repeat('x',60),'p0109-prior',now())
  RETURNING id
)
INSERT INTO bootstrap_enrollment_credentials(id,owner_id,digest,issued_at,expires_at)
SELECT gen_random_uuid(),id,upper(encode(sha256(gen_random_uuid()::text::bytea),'hex')),now(),now()+interval '15 minutes'
FROM prior;
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
[[ "$(latest)" == "$predecessor" && "$(sql -c "SELECT to_regclass('public.admin_sessions') IS NULL")" == t ]]
printf 'API readiness returned 200 on P01-08 without applying schema changes.\n'
bundle >"$artifacts/upgrade.log"
[[ "$(latest)" == "$migration" && "$(predecessor_facts)" == "$before" ]]
printf 'Explicit migration job upgraded P01-08 and preserved a prior identity and enrollment credential.\n'

(
  export DOTNET_ENVIRONMENT=Development
  export LicensingMigrationDatabase__Host=127.0.0.1 LicensingMigrationDatabase__Port="$db_port"
  export LicensingMigrationDatabase__Database=licensing LicensingMigrationDatabase__Username=licensing_migrator
  export LicensingMigrationDatabase__Password=fixture-migrator
  dotnet "$api_dll" bootstrap-admin prepare --verified-email p0109-owner@example.test \
    --verification-reference p0109-local-fixture --admin-origin "$admin"
) >"$artifacts/proof"

PORT="$admin_port" API_ORIGIN="$api" ADMIN_WEB_ROOT="$root/src/admin-frontend/dist/ADMIN/browser" \
  node src/admin-frontend/server.mjs >"$artifacts/web.log" 2>&1 &
web_pid=$!
wait_http "$admin/"
wait_http "$mail/"
(
  cd src/admin-frontend
  export NODE_PATH="$PWD/node_modules"
  export P0109_ADMIN_WEB="$admin" P0109_MAILPIT="$mail" P0109_PROOF_FILE="$artifacts/proof" P0109_ARTIFACTS="$artifacts"
  bun x playwright test --config ../../tests/backend/licensing/browser/playwright.p0109.config.ts
)

facts() {
  sql -c "SELECT count(*) || '|' || md5(string_agg(row_to_json(s)::text, '' ORDER BY s.id))
    FROM admin_sessions s"
}
settled="$(facts)"
[[ "$settled" == 2\|* ]]
bundle >"$artifacts/reapply.log"
[[ "$(latest)" == "$migration" && "$(predecessor_facts)" == "$before" && "$(facts)" == "$settled" ]]
[[ "$(sql -c 'SELECT count(*) FROM "__EFMigrationsHistory"')" == 4 ]]
printf 'Repeated migration job preserved predecessor credentials and both browser-created sessions.\n'
