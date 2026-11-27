#!/usr/bin/env bash
# P01-06 LOCAL FIXTURE. Run after the guarded P01-05 fresh case.
set -euo pipefail
umask 077

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "${repository_root}"
project="${P0105_PROJECT:?Set a dedicated P01-05 fixture project}"
evidence="${P0105_ARTIFACTS:?Set a private fixture evidence directory}"
mkdir -p "${evidence}"
evidence="$(cd "${evidence}" && pwd)"
source "${repository_root}/scripts/dev/licensing-stack-lib.sh"
validate_project_name
for variable in ${!INTELIBILL_@}; do unset "${variable}"; done
assign_ports 14000

if [[ "${1:-}" == --restart ]]; then
  compose restart licensing-api admin-frontend >>"${evidence}/p0106-restart.log" 2>&1
  wait_for licensing-api healthy
  wait_http "${admin_web}/" 200
  exit
fi

[[ $# == 0 ]] || fail "unexpected argument"
[[ "$(state_of licensing-api)" == "running 0 healthy"* ]] || fail "run the fresh case first"
[[ "$(sql intelibill_licensing_dev -c 'select count(*) from admin_identities')" == 0 ]] || fail "fixture already has an admin"
[[ "$(sql intelibill_licensing_dev -c 'select count(*) from initial_admin_bootstrap where prepared_at is not null')" == 0 ]] \
  || fail "fixture already has a prepared proof"
trap 'rm -f "${proof_file}"' EXIT
proof_file="$(mktemp "${evidence}/proof.XXXXXX")"

prepare() {
  # These fixed database login settings are the P01-05 isolated stack's local values.
  compose run -T --rm --no-deps \
    -e DOTNET_ENVIRONMENT=Development \
    -e LicensingMigrationDatabase__Host=postgres \
    -e LicensingMigrationDatabase__Database=intelibill_licensing_dev \
    -e LicensingMigrationDatabase__Username=intelibill_licensing_migrator \
    -e LicensingMigrationDatabase__Password=intelibill_licensing_migrator_password \
    licensing-api bootstrap-admin prepare --verified-email fixture.owner@example.test \
      --verification-reference P01-06-fixture-attestation --admin-origin "${admin_web}"
}

prepare >"${proof_file}" 2>"${evidence}/p0106-prepare.log"
(
  cd src/frontend/licensing
  NODE_PATH="${PWD}/node_modules" P0105_ARTIFACTS="${evidence}" P0106_ADMIN_WEB="${admin_web}" \
    P0106_PROOF_FILE="${proof_file}" P0106_RUNNER="${repository_root}/tests/backend/licensing/acceptance/run-p0106-browser.sh" \
    bun x playwright test --config ../../../tests/backend/licensing/browser/playwright.p0106.config.ts
)
rm -f "${proof_file}"

reprepare_status=0
prepare >"${proof_file}" 2>"${evidence}/p0106-reprepare.log" || reprepare_status=$?
[[ "${reprepare_status}" == 2 && ! -s "${proof_file}" ]] || fail "consumed setup re-preparation was not safely refused"
grep -q 'Initial setup is closed' "${evidence}/p0106-reprepare.log"

facts="$(sql intelibill_licensing_dev -c "
  SELECT
    (SELECT count(*) FROM admin_identities WHERE verified_at IS NOT NULL AND mfa_enrolled_at IS NULL AND length(password_hash) = 60),
    (SELECT count(*) FROM initial_admin_bootstrap WHERE consumed_at IS NOT NULL AND owner_id IS NOT NULL AND length(proof_digest) = 64),
    (SELECT count(*) FROM bootstrap_enrollment_credentials WHERE revoked_at IS NULL AND length(digest) = 64),
    (SELECT count(*) FROM bootstrap_enrollment_credentials WHERE revoked_at IS NOT NULL),
    (SELECT count(*) FROM provider_audit_events WHERE action = 3),
    (SELECT count(*) FROM provider_audit_events WHERE action = 5)")"
[[ "${facts}" == '1|1|1|1|1|1' ]] || fail "unexpected bootstrap facts: ${facts}"
pass "P01-06 LOCAL FIXTURE: verified pending owner | consumed proof | active enrollment | revoked enrollment | owner audit | resume audit = ${facts}"
