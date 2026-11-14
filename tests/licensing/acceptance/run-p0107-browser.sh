#!/usr/bin/env bash
# P01-07 LOCAL FIXTURE. Run after the guarded P01-05 fresh case and the P01-06 runner.
set -euo pipefail
umask 077

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "${repository_root}"
project="${P0105_PROJECT:?Set a dedicated P01-05 fixture project}"
evidence="${P0105_ARTIFACTS:?Set a private fixture evidence directory}"
mkdir -p "${evidence}"
evidence="$(cd "${evidence}" && pwd)"
source "${repository_root}/scripts/dev/licensing-stack-lib.sh"
validate_project_name
for variable in ${!INTELIBILL_@}; do unset "${variable}"; done
assign_ports 14000

[[ $# == 0 ]] || fail "unexpected argument"
[[ "$(state_of licensing-api)" == "running 0 healthy"* ]] || fail "run the fresh case first"
owner="$(sql intelibill_licensing_dev -c 'select owner_id from initial_admin_bootstrap where consumed_at is not null')"
[[ -n "${owner}" ]] || fail "run the P01-06 runner first"
[[ "$(sql intelibill_licensing_dev -c 'select count(*) from admin_invitations')" == 0 ]] || fail "fixture already has invitations"

session_file="$(mktemp "${evidence}/session.XXXXXX")"
restore() {
  rm -f "${session_file}"
  # Recreate the API without the fixture so the stack ends fail-closed.
  unset INTELIBILL_ADMIN_FIXTURE_SESSION_DIGEST INTELIBILL_ADMIN_FIXTURE_ADMIN_ID INTELIBILL_ADMIN_FIXTURE_CAPABILITIES
  compose up -d --no-deps licensing-api >>"${evidence}/p0107-restore.log" 2>&1 || true
}
trap restore EXIT

# FIXTURE ONLY: P01-08 owns TOTP. Mark the bootstrapped owner as MFA-enrolled for this stack.
sql intelibill_licensing_dev -c "update admin_identities set mfa_enrolled_at = verified_at where id = '${owner}'" >/dev/null
openssl rand -base64 48 | tr '+/' '-_' | tr -d '=\n' >"${session_file}"
export INTELIBILL_ADMIN_FIXTURE_SESSION_DIGEST="$(tr -d '\n' <"${session_file}" | openssl dgst -sha256 -hex | awk '{print toupper($NF)}')"
export INTELIBILL_ADMIN_FIXTURE_ADMIN_ID="${owner}"
export INTELIBILL_ADMIN_FIXTURE_CAPABILITIES="admin.invitations.manage"
compose up -d --no-deps licensing-api >>"${evidence}/p0107-fixture.log" 2>&1
wait_for licensing-api healthy
wait_http "${admin_web}/" 200

(
  cd src/admin-frontend
  NODE_PATH="${PWD}/node_modules" P0105_ARTIFACTS="${evidence}" P0107_ADMIN_WEB="${admin_web}" \
    P0107_MAILPIT="${mailpit_ui}" P0107_SESSION_FILE="${session_file}" \
    bun x playwright test --config ../../tests/licensing/browser/playwright.p0107.config.ts
)

facts="$(sql intelibill_licensing_dev -c "
  SELECT
    (SELECT count(*) FROM admin_invitations WHERE accepted_at IS NOT NULL AND length(token_digest) = 64),
    (SELECT count(*) FROM admin_identities a JOIN admin_invitations i ON i.accepted_admin_id = a.id
       WHERE a.mfa_enrolled_at IS NULL AND a.verification_reference = 'invitation:' || i.id),
    (SELECT count(*) FROM bootstrap_enrollment_credentials c JOIN admin_invitations i ON i.accepted_admin_id = c.owner_id),
    (SELECT count(*) FROM provider_audit_events WHERE action = 7),
    (SELECT count(*) FROM provider_audit_events WHERE action = 8)")"
[[ "${facts}" == '1|1|1|1|1' ]] || fail "unexpected invitation facts: ${facts}"
leaks="$(compose logs --no-color licensing-api admin-frontend 2>&1 | grep -c -E 'i1_[A-Za-z0-9_-]{20}|example\.test' || true)"
[[ "${leaks}" == 0 ]] || fail "service logs mention an invitation token or the invitee"
pass "P01-07 LOCAL FIXTURE: accepted invitation | pending invited admin | enrollment credential | issued audit | accepted audit = ${facts}; token/invitee log matches = ${leaks}"

restore
trap - EXIT
wait_for licensing-api healthy
status="$(curl -s -o /dev/null -w '%{http_code}' --cookie "Intelibill.Admin.FixtureSession=unused" "${admin_web}/api/admin/invitations/context")"
[[ "${status}" == 401 ]] || fail "invitation context answered ${status} after the fixture was removed"
pass "P01-07 LOCAL FIXTURE removed: invitation context -> ${status}"
