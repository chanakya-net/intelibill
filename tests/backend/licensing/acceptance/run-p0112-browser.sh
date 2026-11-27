#!/usr/bin/env bash
# Disposable PostgreSQL, explicit migration image, source licensing API and production admin web.
set -euo pipefail
umask 077

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$root"
artifacts="$(mktemp -d "${TMPDIR:-/tmp}/intelibill-p0112.XXXXXX")"
artifacts="$(cd "$artifacts" && pwd -P)"
project="intelibill-p0112-${artifacts##*.}"
image="$(printf '%s' "$project" | tr '[:upper:]' '[:lower:]')-migrate:local"
api_dll="$root/src/backend/licensing/Intelibill.Licensing.Api/bin/Debug/net10.0/Intelibill.Licensing.Api.dll"
predecessor=20261114225609_AddAdminSessionRevocationAudit
migration=20261115002745_ProtectAdminRecovery
predecessor_ref=be9e347661f70672b5d7ec3467ae36a569b38677
predecessor_src="$artifacts/predecessor"
api_pid= web_pid= predecessor_pid=

cleanup() {
  for pid in "$web_pid" "$api_pid" "$predecessor_pid"; do
    if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  done
  docker rm -fv "$project-db" "$project-mail" >/dev/null 2>&1 || true
  docker network rm "$project" >/dev/null 2>&1 || true
  docker image rm "$image" >/dev/null 2>&1 || true
  rm -f "$artifacts/proof"
  rm -rf "$artifacts/keyring" "$predecessor_src"
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

# Explicit admin_totp columns: the upgrade adds retired_at, checked separately to stay NULL for prior facts.
predecessor_facts() {
  sql -c "SELECT md5(concat(
    (SELECT (to_jsonb(a) - 'recovery_continuation_digest')::text FROM admin_identities a
      WHERE verification_reference='p0112-prior'),
    (SELECT concat_ws('|', t.admin_id, t.key_id, encode(t.encrypted_secret,'hex'), t.last_accepted_step, t.attempts)
      FROM admin_totp t JOIN admin_identities a ON a.id=t.admin_id WHERE a.verification_reference='p0112-prior'),
    (SELECT string_agg(row_to_json(r)::text, '' ORDER BY r.digest) FROM admin_recovery_codes r
      JOIN admin_identities a ON a.id=r.admin_id WHERE a.verification_reference='p0112-prior'),
    (SELECT row_to_json(s)::text FROM admin_sessions s JOIN admin_identities a ON a.id=s.admin_id
      WHERE a.verification_reference='p0112-prior'),
    (SELECT row_to_json(e)::text FROM provider_audit_events e WHERE id='00000000-0000-0000-0000-000000008421')))"
}

printf 'P01-12 fixture output in %s\n' "$artifacts"
mkdir "$predecessor_src"
git archive "$predecessor_ref" global.json Directory.Build.props Directory.Packages.props src/licensing src/shared/backend \
  | tar -x -C "$predecessor_src"
predecessor_api="$predecessor_src/src/licensing/Intelibill.Licensing.Api"
dotnet build "$predecessor_api" -warnaserror >"$artifacts/predecessor-build.log"
dotnet build src/backend/licensing/Intelibill.Licensing.Api -warnaserror >"$artifacts/api-build.log"
(cd src/admin-frontend && bun run build --output-path "$artifacts/admin") >"$artifacts/web-build.log"
docker build -f src/backend/licensing/Dockerfile.migrate -t "$image" . >"$artifacts/bundle-build.log" 2>&1

docker network create "$project" >/dev/null
docker run -d --name "$project-db" --network "$project" --network-alias postgres \
  -p 127.0.0.1::5432 -e POSTGRES_PASSWORD=fixture-bootstrap postgres:17-alpine >/dev/null
docker run -d --name "$project-mail" -p 127.0.0.1::1025 -p 127.0.0.1::8025 axllent/mailpit:v1.33 >/dev/null
db_port="$(docker port "$project-db" 5432/tcp | cut -d: -f2)"
smtp_port="$(docker port "$project-mail" 1025/tcp | cut -d: -f2)"
mail="http://$(docker port "$project-mail" 8025/tcp)"
read -r api_port admin_port predecessor_port < <(python3 - <<'PY'
import socket
with socket.socket() as a, socket.socket() as b, socket.socket() as c:
    for listener in (a, b, c):
        listener.bind(('127.0.0.1', 0))
    print(*(listener.getsockname()[1] for listener in (a, b, c)))
PY
)
api="http://127.0.0.1:$api_port"
old_api="http://127.0.0.1:$predecessor_port"
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
  VALUES(gen_random_uuid(),'PRIOR@EXAMPLE.TEST',repeat('x',60),'p0112-prior',now()-interval '1 hour',now()-interval '1 hour')
  RETURNING id
), totp AS (
  INSERT INTO admin_totp(admin_id,key_id,encrypted_secret,last_accepted_step,attempts)
  SELECT id,'prior',decode(repeat('ab',48),'hex'),1,0 FROM prior RETURNING admin_id
)
INSERT INTO admin_recovery_codes(admin_id,digest) SELECT admin_id,repeat('C',64) FROM totp;
INSERT INTO admin_sessions(id,admin_id,created_at,mfa_verified_at,last_seen_at)
SELECT gen_random_uuid(),id,now()-interval '10 minutes',now()-interval '10 minutes',now()-interval '5 minutes'
FROM admin_identities WHERE verification_reference='p0112-prior';
INSERT INTO provider_audit_events(id,action,occurred_at,owner_id,actor_kind,reason,correlation_id)
SELECT '00000000-0000-0000-0000-000000008421',14,now()-interval '10 minutes',id,4,13,gen_random_uuid()
FROM admin_identities WHERE verification_reference='p0112-prior';
SQL
before="$(predecessor_facts)"

start_api() {
  local dll="$1" origin="$2"
  cd "$(dirname "$dll")"
  export ASPNETCORE_ENVIRONMENT=Development
  export LicensingDatabase__Host=127.0.0.1 LicensingDatabase__Port="$db_port"
  export LicensingDatabase__Database=licensing LicensingDatabase__Username=licensing_runtime
  export LicensingDatabase__Password=fixture-runtime
  export AdminMail__Host=127.0.0.1 AdminMail__Port="$smtp_port" AdminMail__LocalCapture=true
  export AdminMail__FromEmail=provider-console@example.test
  export AdminBootstrap__DataProtectionKeyDirectory="$artifacts/keyring"
  export AdminMfa__ActiveKeyId=fixture AdminMfa__Keys__fixture="$fixture_mfa_key"
  export AdminSessions__SigningKey="$fixture_signing_key"
  exec dotnet "$dll" --urls "$origin"
}

fixture_mfa_key="$(openssl rand -base64 32)"
fixture_signing_key="$(openssl rand -base64 32)"
start_api "$predecessor_api/bin/Debug/net10.0/Intelibill.Licensing.Api.dll" "$old_api" >"$artifacts/predecessor-api.log" 2>&1 &
predecessor_pid=$!
wait_http "$old_api/health/ready"
start_api "$api_dll" "$api" >"$artifacts/api.log" 2>&1 &
api_pid=$!
wait_http "$api/health/ready"
[[ "$(latest)" == "$predecessor" ]]
printf 'Source API became ready on P01-11 without applying the recovery migration.\n'
bundle >"$artifacts/upgrade.log"
[[ "$(latest)" == "$migration" && "$(predecessor_facts)" == "$before" ]]
[[ "$(sql -c "SELECT count(*) FROM admin_totp WHERE retired_at IS NULL")" == 1 ]]
printf 'Explicit migration job upgraded P01-11 and preserved prior identity, authenticator, recovery code, session and audit facts.\n'
wait_http "$old_api/health/ready"
kill "$predecessor_pid"
wait "$predecessor_pid" || test "$?" -eq 143
predecessor_pid=
if curl -fsS --max-time 3 "$old_api/health/ready" >/dev/null 2>&1; then exit 1; fi
printf 'Predecessor and recovery-aware APIs shared the upgraded database. Predecessor was stopped and unreachable before recovery.\n'


(
  export DOTNET_ENVIRONMENT=Development
  export LicensingMigrationDatabase__Host=127.0.0.1 LicensingMigrationDatabase__Port="$db_port"
  export LicensingMigrationDatabase__Database=licensing LicensingMigrationDatabase__Username=licensing_migrator
  export LicensingMigrationDatabase__Password=fixture-migrator
  dotnet "$api_dll" bootstrap-admin prepare --verified-email p0112-owner@example.test \
    --verification-reference p0112-local-fixture --admin-origin "$admin"
) >"$artifacts/proof"

PORT="$admin_port" API_ORIGIN="$api" ADMIN_WEB_ROOT="$artifacts/admin/browser" \
  node src/admin-frontend/server.mjs >"$artifacts/web.log" 2>&1 &
web_pid=$!
wait_http "$admin/"
wait_http "$mail/"
(
  cd src/admin-frontend
  export NODE_PATH="$PWD/node_modules"
  export P0112_ADMIN_WEB="$admin" P0112_MAILPIT="$mail" P0112_PROOF_FILE="$artifacts/proof" P0112_ARTIFACTS="$artifacts"
  bun x playwright test --config ../../tests/backend/licensing/browser/playwright.p0112.config.ts
)

[[ "$(sql -c "SELECT count(*) FROM admin_recoveries WHERE kind=1 AND redeemed_at IS NOT NULL")" == 1 ]]
[[ "$(sql -c "SELECT count(*) FROM admin_recoveries WHERE kind=2 AND redeemed_at IS NOT NULL AND evidence_reference='SEC-2026-0412'")" == 1 ]]
[[ "$(sql -c "SELECT count(*) FROM provider_audit_events WHERE action=18 AND reason=31")" -ge 2 ]]
for secret_column in "string_agg(to_jsonb(e)::text,'') FROM provider_audit_events e"; do
  if sql -c "SELECT $secret_column" | rg -qi 'k1_|SEC-2026-0412|@example\.test'; then exit 1; fi
done
printf 'Recovery facts persisted; session revocations audited; audit rows hold no reset code, evidence reference or address.\n'

settled="$(sql -c "SELECT md5(string_agg(row_to_json(e)::text, '' ORDER BY e.id)) FROM provider_audit_events e")"
if bundle "$predecessor" >"$artifacts/down.log" 2>&1; then exit 1; fi
rg -q '55000: Administrator recovery evidence exists' "$artifacts/down.log"
[[ "$(latest)" == "$migration" ]]
bundle >"$artifacts/reapply.log"
rg -q 'already up to date' "$artifacts/reapply.log"
[[ "$(latest)" == "$migration" && "$(predecessor_facts)" == "$before" ]]
[[ "$(sql -c "SELECT md5(string_agg(row_to_json(e)::text, '' ORDER BY e.id)) FROM provider_audit_events e")" == "$settled" ]]
printf 'Down refused with SQLSTATE 55000 once recovery evidence existed; reapply kept every fact and audit row.\n'
if curl -fsS --max-time 3 "$old_api/health/ready" >/dev/null 2>&1; then exit 1; fi
wait_http "$api/health/ready"
printf 'Rollback kept the recovery-aware API serving and the predecessor unreachable.\n'
