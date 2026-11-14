#!/usr/bin/env bash
# Disposable PostgreSQL and Mailpit, explicit migration image, source API and production admin web.
set -euo pipefail
umask 077

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "${repository_root}"
evidence="$(mktemp -d "${TMPDIR:-/tmp}/intelibill-p0108.XXXXXX")"
project="intelibill-p0108-${evidence##*.}"
api_project=src/licensing/Intelibill.Licensing.Api
api_dll="${repository_root}/${api_project}/bin/Debug/net10.0/Intelibill.Licensing.Api.dll"
job_image="$(printf %s "${project}" | tr A-Z a-z)-migrate:local"
predecessor=20261114181011_AddAdminInvitations
migration=20261114185900_AddAdminMfaEnrollment
api_pid= web_pid= browser_pid=

cleanup() {
  for pid in "${browser_pid}" "${web_pid}" "${api_pid}"; do
    if [[ -n "${pid}" ]]; then kill "${pid}" 2>/dev/null || true; wait "${pid}" 2>/dev/null || true; fi
  done
  docker rm -fv "${project}-db" "${project}-mail" >/dev/null 2>&1 || true
  docker image rm "${job_image}" >/dev/null 2>&1 || true
  docker network rm "${project}" >/dev/null 2>&1 || true
  rm -f "${evidence}/proof" "${evidence}/fixture-session"
  rm -rf "${evidence}/csrf-keyring"
}
trap cleanup EXIT

wait_http() {
  for ((i=0;i<100;i++)); do
    if curl -fsS -o /dev/null "$1"; then return 0; fi
    sleep .2
  done 2>/dev/null
  printf 'Fixture endpoint not ready: %s\n' "$1" >&2
  return 1
}

sql() {
  docker exec -i "${project}-db" psql -XqAt -v ON_ERROR_STOP=1 -U postgres -d licensing "$@"
}

bundle() {
  docker run --rm --network "${project}" \
    -e LicensingMigrationDatabase__Host=postgres -e LicensingMigrationDatabase__Database=licensing \
    -e LicensingMigrationDatabase__Username=licensing_migrator -e LicensingMigrationDatabase__Password=fixture-migrator \
    "${job_image}" "$@"
}

allocate_http_ports() {
  python3 - <<'PY'
import socket
with socket.socket() as a, socket.socket() as b:
    a.bind(('127.0.0.1', 0))
    b.bind(('127.0.0.1', 0))
    print(a.getsockname()[1], b.getsockname()[1])
PY
}

start_api() {
  (
    cd "${api_project}"
    export ASPNETCORE_ENVIRONMENT=Development
    export LicensingDatabase__Host=127.0.0.1 LicensingDatabase__Port="${db_port}"
    export LicensingDatabase__Database=licensing LicensingDatabase__Username=licensing_runtime
    export LicensingDatabase__Password=fixture-runtime
    export AdminMail__Host=127.0.0.1 AdminMail__Port="${smtp_port}" AdminMail__LocalCapture=true
    export AdminMail__FromEmail=provider-console@example.test
    export AdminBootstrap__DataProtectionKeyDirectory="${evidence}/csrf-keyring"
    export Observability__Logging__MinimumLevel=Verbose
    exec dotnet "${api_dll}" --urls "${api}"
  ) >>"${evidence}/api.log" 2>&1 &
  api_pid=$!
  wait_http "${api}/health/ready"
}

pending_facts() {
  sql -c "SELECT count(*) || '|' || md5(string_agg(row_to_json(c)::text, ''))
    FROM bootstrap_enrollment_credentials c
    JOIN admin_identities a ON a.id = c.owner_id
    WHERE a.mfa_enrolled_at IS NULL AND c.revoked_at IS NULL"
}

start_browser() {
  (
    cd src/admin-frontend
    export NODE_PATH="${PWD}/node_modules"
    export P0108_ARTIFACTS="${evidence}" P0108_ADMIN_WEB="${admin}" P0108_MAILPIT="${mail}"
    export P0108_PROOF_FILE="${evidence}/proof" P0108_SESSION_FILE="${evidence}/fixture-session"
    export P0108_API_LOG="${evidence}/api.log"
    export P0108_OWNER_ENROLLED="${evidence}/owner-enrolled" P0108_INVITER_READY="${evidence}/inviter-ready"
    exec bun x playwright test --config ../../tests/licensing/browser/playwright.p0108.config.ts
  ) >"${evidence}/browser.log" 2>&1 &
  browser_pid=$!
}

wait_owner() {
  for ((i=0;i<100;i++)); do
    [[ -f "${evidence}/owner-enrolled" ]] && return 0
    kill -0 "${browser_pid}" 2>/dev/null || break
    sleep .1
  done
  printf 'Owner enrollment failed. See %s/browser.log\n' "${evidence}" >&2
  return 1
}

printf 'P01-08 fixture output in %s\n' "${evidence}"
dotnet build "${api_project}" -warnaserror >"${evidence}/api-build.log"
(cd src/admin-frontend && bun run build) >"${evidence}/web-build.log"
docker build -f src/licensing/Dockerfile.migrate -t "${job_image}" . >"${evidence}/bundle-build.log" 2>&1

docker network create "${project}" >/dev/null
docker run -d --name "${project}-db" --network "${project}" --network-alias postgres \
  -p 127.0.0.1::5432 -e POSTGRES_PASSWORD=fixture-bootstrap postgres:17-alpine >/dev/null
docker run -d --name "${project}-mail" --network "${project}" \
  -p 127.0.0.1::1025 -p 127.0.0.1::8025 axllent/mailpit:v1.33 >/dev/null
db_port="$(docker port "${project}-db" 5432/tcp | cut -d: -f2)"
smtp_port="$(docker port "${project}-mail" 1025/tcp | cut -d: -f2)"
mail="http://$(docker port "${project}-mail" 8025/tcp)"
read -r api_port admin_port <<<"$(allocate_http_ports)"
api="http://127.0.0.1:${api_port}"
admin="http://127.0.0.1:${admin_port}"

for ((i=0;i<100;i++)); do
  if docker exec "${project}-db" pg_isready -h 127.0.0.1 -U postgres >/dev/null; then break; fi
  sleep .2
done
docker exec -i "${project}-db" psql -Xq -v ON_ERROR_STOP=1 -U postgres <<'SQL'
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
bundle "${predecessor}" >"${evidence}/predecessor.log"
actual="$(sql -c 'select max(migration_id) from "__EFMigrationsHistory"')"
[[ "${actual}" == "${predecessor}" ]]
# Preserve a pre-existing pending identity and one enrollment grant independently of browser fixtures.
sql <<'SQL'
INSERT INTO admin_identities (id, email, password_hash, verification_reference, verified_at)
VALUES ('20000000-0000-4000-8000-000000000008', 'P0108-PENDING@EXAMPLE.TEST', repeat('x',60), 'fixture', now());
INSERT INTO bootstrap_enrollment_credentials (id,owner_id,digest,issued_at,expires_at)
VALUES ('30000000-0000-4000-8000-000000000008','20000000-0000-4000-8000-000000000008',
  upper(encode(sha256(gen_random_uuid()::text::bytea),'hex')),now(),now()+interval '15 minutes');
SQL
before="$(pending_facts)"
[[ "${before}" == 1\|* ]]
export AdminMfa__ActiveKeyId=fixture
export AdminMfa__Keys__fixture="$(openssl rand -base64 32)"
start_api
actual="$(sql -c 'select max(migration_id) from "__EFMigrationsHistory"')"
[[ "${actual}" == "${predecessor}" ]]
[[ "$(sql -c "select to_regclass('public.admin_totp') is null")" == t ]]
printf 'API readiness 200 on predecessor; schema remained at P01-07.\n'
bundle >"${evidence}/upgrade.log"
actual="$(sql -c 'select max(migration_id) from "__EFMigrationsHistory"')"
[[ "${actual}" == "${migration}" ]]
[[ "$(pending_facts)" == "${before}" ]]
printf 'Migration job upgraded the exact predecessor and preserved its pending credential.\n'

(
  export DOTNET_ENVIRONMENT=Development
  export LicensingMigrationDatabase__Host=127.0.0.1 LicensingMigrationDatabase__Port="${db_port}"
  export LicensingMigrationDatabase__Database=licensing LicensingMigrationDatabase__Username=licensing_migrator
  export LicensingMigrationDatabase__Password=fixture-migrator
  dotnet "${api_dll}" bootstrap-admin prepare --verified-email p0108-owner@example.test \
    --verification-reference p0108-local-fixture --admin-origin "${admin}"
) >"${evidence}/proof"

PORT="${admin_port}" API_ORIGIN="${api}" ADMIN_WEB_ROOT="${repository_root}/src/admin-frontend/dist/ADMIN/browser" \
  node src/admin-frontend/server.mjs >"${evidence}/web.log" 2>&1 &
web_pid=$!
wait_http "${admin}/"
wait_http "${mail}/"
start_browser
wait_owner

# Use the P01-07 guarded provider fixture after the real owner MFA completion.
kill "${api_pid}"
wait "${api_pid}" || true
api_pid=
openssl rand -hex 32 >"${evidence}/fixture-session"
export AdminFixtures__SessionDigest="$(tr -d '\n' <"${evidence}/fixture-session" | openssl dgst -sha256 -hex | awk '{print toupper($NF)}')"
export AdminFixtures__AdminId="$(sql -c 'select owner_id from initial_admin_bootstrap')"
export AdminFixtures__Capabilities=admin.invitations.manage
start_api
touch "${evidence}/inviter-ready"
if ! wait "${browser_pid}"; then
  printf 'Browser verification failed. See %s/browser.log\n' "${evidence}" >&2
  exit 1
fi
browser_pid=
cat "${evidence}/browser.log"

facts="$(sql -c "SELECT
 (SELECT count(*) FROM admin_identities WHERE mfa_enrolled_at IS NOT NULL),
 (SELECT count(*) FROM admin_totp WHERE octet_length(encrypted_secret)=48 AND last_accepted_step IS NOT NULL),
 (SELECT count(*) FROM admin_recovery_codes WHERE length(digest)=64 AND consumed_at IS NULL),
 (SELECT count(*) FROM bootstrap_enrollment_credentials WHERE revoked_at IS NOT NULL),
 (SELECT count(*) FROM provider_audit_events WHERE action=12),
 (SELECT count(*) FROM provider_audit_events WHERE action=13),
 (SELECT count(*) FROM admin_invitations WHERE accepted_at IS NOT NULL)")"
[[ "${facts}" == '2|2|20|2|2|2|1' ]]
checksum() {
  sql -c "SELECT md5(concat(
   (SELECT string_agg(row_to_json(t)::text, '' ORDER BY admin_id) FROM admin_totp t),
   (SELECT string_agg(row_to_json(r)::text, '' ORDER BY admin_id, digest) FROM admin_recovery_codes r),
   (SELECT string_agg(row_to_json(c)::text, '' ORDER BY id) FROM bootstrap_enrollment_credentials c),
   (SELECT string_agg(row_to_json(a)::text, '' ORDER BY id) FROM provider_audit_events a)))"
}
settled="$(checksum)"
bundle >"${evidence}/reapply.log"
[[ "$(pending_facts)" == "${before}" && "$(checksum)" == "${settled}" ]]
[[ "$(curl -s -o /dev/null -w '%{http_code}' "${admin}/api/admin/access")" == 401 ]]
printf 'Repeated migration job preserved enrollment, recovery, audit and pending facts.\n'
printf 'Enrolled | encrypted | hashed codes | retired grants | completed audits | rejected audits | accepted invite = %s\n' "${facts}"
