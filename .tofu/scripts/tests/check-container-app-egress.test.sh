#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHECKER="${SCRIPT_DIR}/../check-container-app-egress.sh"
FIXTURES="${SCRIPT_DIR}/fixtures"
TEST_TMP="$(mktemp -d)"
trap 'rm -rf -- "${TEST_TMP}"' EXIT

pass_count=0

pass() {
  printf 'ok - %s\n' "$1"
  pass_count=$((pass_count + 1))
}

fail() {
  printf 'not ok - %s\n' "$1" >&2
  if [[ -s "${TEST_TMP}/stderr" ]]; then
    sed 's/^/  stderr: /' "${TEST_TMP}/stderr" >&2
  fi
  if [[ -s "${TEST_TMP}/stdout" ]]; then
    sed 's/^/  stdout: /' "${TEST_TMP}/stdout" >&2
  fi
  exit 1
}

run_case() {
  : >"${TEST_TMP}/stdout"
  : >"${TEST_TMP}/stderr"
  set +e
  "$@" >"${TEST_TMP}/stdout" 2>"${TEST_TMP}/stderr"
  case_status=$?
  set -e
}

expect_success() {
  local name="$1"
  shift
  run_case "$@"
  [[ "${case_status}" -eq 0 ]] || fail "${name} (expected success, got ${case_status})"
  pass "${name}"
}

expect_failure() {
  local name="$1"
  shift
  run_case "$@"
  [[ "${case_status}" -ne 0 ]] || fail "${name} (expected failure)"
  pass "${name}"
}

fixture_args=(
  --advertised-file "${FIXTURES}/advertised-ok.json"
  --firewall-file "${FIXTURES}/firewall-ok.json"
)

expect_success \
  "matching exact managed rules" \
  "${CHECKER}" "${fixture_args[@]}"

mkdir "${TEST_TMP}/no-az-bin"
for fixture_command in bash jq mktemp rm; do
  ln -s "$(command -v "${fixture_command}")" "${TEST_TMP}/no-az-bin/${fixture_command}"
done
expect_success \
  "fixture mode does not require Azure CLI" \
  env PATH="${TEST_TMP}/no-az-bin" "${CHECKER}" "${fixture_args[@]}"

expect_failure \
  "empty advertised set fails even without retained addresses" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-empty.json" \
  --firewall-file "${FIXTURES}/firewall-ok.json"

expect_failure \
  "empty advertised set fails even with retained addresses" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-empty.json" \
  --retained-file "${FIXTURES}/retained.json" \
  --firewall-file "${FIXTURES}/firewall-stale.json"

expect_failure \
  "missing managed address is not satisfied by operator rule" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${FIXTURES}/firewall-missing.json"

expect_failure \
  "broad operator rule fails regardless of name" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${FIXTURES}/firewall-broad.json"

expect_failure \
  "non-identical managed range fails" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${FIXTURES}/firewall-malformed.json"

expect_failure \
  "unreviewed stale managed rule fails" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --firewall-file "${FIXTURES}/firewall-stale.json"

expect_success \
  "reviewed retained managed rule succeeds" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --retained-file "${FIXTURES}/retained.json" \
  --firewall-file "${FIXTURES}/firewall-stale.json"

expect_success \
  "dev-only selection ignores prod managed rules" \
  "${CHECKER}" "${fixture_args[@]}" --environment dev

expect_failure \
  "invalid environment fails" \
  "${CHECKER}" "${fixture_args[@]}" --environment staging

expect_failure \
  "repeated environment fails" \
  "${CHECKER}" "${fixture_args[@]}" --environment dev --environment dev

cat >"${TEST_TMP}/advertised-leading-zero.json" <<'JSON'
{"dev":["20.10.0.01"],"prod":["20.20.0.1"]}
JSON
expect_failure \
  "non-canonical advertised IPv4 fails" \
  "${CHECKER}" \
  --advertised-file "${TEST_TMP}/advertised-leading-zero.json" \
  --firewall-file "${FIXTURES}/firewall-ok.json"

cat >"${TEST_TMP}/retained-sentinel.json" <<'JSON'
{"dev":["000.000.000.000"],"prod":[]}
JSON
expect_failure \
  "disguised retained sentinel fails" \
  "${CHECKER}" \
  --advertised-file "${FIXTURES}/advertised-ok.json" \
  --retained-file "${TEST_TMP}/retained-sentinel.json" \
  --firewall-file "${FIXTURES}/firewall-ok.json"

mkdir "${TEST_TMP}/fake-bin"
cat >"${TEST_TMP}/fake-bin/az" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${AZ_TEST_MODE:-success}" == "lookup-failure" && "$1 $2" == "containerapp show" ]]; then
  printf 'sensitive lookup detail must not escape\n' >&2
  exit 23
fi
if [[ "${AZ_TEST_MODE:-success}" == "non-array" && "$1 $2" == "containerapp job" ]]; then
  printf '{"unexpected":"shape"}\n'
  exit 0
fi
case "$1 $2" in
  "containerapp show")
    case " $* " in
      *" intelibill-dev-api "*) printf '["20.10.0.1"]\n' ;;
      *" intelibill-dev-web "*) printf '["20.10.0.2"]\n' ;;
      *" intelibill-prod-api "*|*" intelibill-prod-web "*) printf '["20.20.0.1"]\n' ;;
      *) exit 24 ;;
    esac
    ;;
  "containerapp job")
    case " $* " in
      *" intelibill-dev-migrate "*) printf '["20.10.0.3"]\n' ;;
      *" intelibill-prod-migrate "*) printf '["20.20.0.1"]\n' ;;
      *) exit 24 ;;
    esac
    ;;
  "postgres flexible-server")
    printf '%s\n' \
      '[{"name":"container-apps-dev-20-10-0-1","startIpAddress":"20.10.0.1","endIpAddress":"20.10.0.1"},{"name":"container-apps-dev-20-10-0-2","startIpAddress":"20.10.0.2","endIpAddress":"20.10.0.2"},{"name":"container-apps-dev-20-10-0-3","startIpAddress":"20.10.0.3","endIpAddress":"20.10.0.3"},{"name":"container-apps-prod-20-20-0-1","startIpAddress":"20.20.0.1","endIpAddress":"20.20.0.1"}]'
    ;;
  *) exit 25 ;;
esac
SH
chmod +x "${TEST_TMP}/fake-bin/az"

expect_success \
  "live discovery unions api web and migration arrays" \
  env PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"

expect_failure \
  "live lookup failure fails closed" \
  env AZ_TEST_MODE=lookup-failure PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"
if rg -q 'sensitive lookup detail' "${TEST_TMP}/stdout" "${TEST_TMP}/stderr"; then
  fail "live lookup failure redacts Azure CLI diagnostics"
fi
pass "live lookup failure redacts Azure CLI diagnostics"

expect_failure \
  "live non-array response fails closed" \
  env AZ_TEST_MODE=non-array PATH="${TEST_TMP}/fake-bin:${PATH}" "${CHECKER}"

printf '1..%d\n' "${pass_count}"
